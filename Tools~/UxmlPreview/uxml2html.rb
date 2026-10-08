#!/usr/bin/env ruby
# Converts a UI Toolkit UXML file (plus the USS files it references) into a standalone HTML page
# that approximates how Unity renders it. Both systems lay out with flexbox, so the mapping is
# mostly mechanical. Deviations are listed in README.md.
#
# Usage: ruby uxml2html.rb <file.uxml> <out.html> [--width 1280] [--height 720]

require 'rexml/document'
require 'cgi'
require 'base64'

STAGE_WIDTH_DEFAULT = 1280
STAGE_HEIGHT_DEFAULT = 720
STAGE_BACKGROUND = '#0b0d14'
ELEMENT_PREFIX = 'uxml-'
DEFAULT_FONT_STACK = '"Segoe UI", Arial, sans-serif'
DEFAULT_FONT_SIZE_PX = 12
UNITY_TEXT_ALIGN_VERTICAL = { 'upper' => 'flex-start', 'middle' => 'center', 'lower' => 'flex-end' }.freeze
UNITY_TEXT_ALIGN_HORIZONTAL = { 'left' => 'flex-start', 'center' => 'center', 'right' => 'flex-end' }.freeze

class UssConverter
  attr_reader :font_faces

  def initialize
    @font_faces = {} # family name => @font-face rule
    @uss_dir = '.'
  end

  # USS -> CSS declarations. Returns a CSS string.
  def convert(uss, uss_dir = '.')
    @uss_dir = uss_dir
    css = uss.gsub(%r{/\*.*?\*/}m, '')
    css = css.gsub(/@import[^;]*;/, '')
    css.gsub(/([^{}]+)\{([^{}]*)\}/m) do
      selector = convert_selector(Regexp.last_match(1).strip)
      body = convert_declarations(Regexp.last_match(2))
      "#{selector} {#{body}}\n"
    end
  end

  def convert_selector(sel)
    sel.split(',').map do |part|
      part.strip
          .gsub(/(?<![\w.#-])([A-Z][A-Za-z]*)(?![\w-])/) { ELEMENT_PREFIX + Regexp.last_match(1).downcase } # type selectors
          .gsub(':root', '.uxml-stage')
    end.join(', ')
  end

  def convert_declarations(block)
    out = []
    block.split(';').each do |decl|
      prop, value = decl.split(':', 2)
      next if prop.nil? || value.nil?
      prop = prop.strip
      value = value.strip
      out.concat(convert_declaration(prop, value))
    end
    out.empty? ? '' : ' ' + out.join('; ') + '; '
  end

  # Returns an array of "prop: value" strings.
  def convert_declaration(prop, value)
    case prop
    when '-unity-font-style'
      case value
      when 'bold' then ['font-weight: 700']
      when 'italic' then ['font-style: italic']
      when 'bold-and-italic' then ['font-weight: 700', 'font-style: italic']
      else ['font-weight: 400', 'font-style: normal']
      end
    when '-unity-text-align'
      vertical, horizontal = value.split('-')
      ["justify-content: #{UNITY_TEXT_ALIGN_VERTICAL[vertical]}",
       "align-items: #{UNITY_TEXT_ALIGN_HORIZONTAL[horizontal]}",
       "text-align: #{horizontal}"]
    when 'white-space'
      # Unity keeps explicit newlines in both modes.
      [value == 'nowrap' ? 'white-space: pre' : 'white-space: pre-wrap']
    when 'rotate'
      ["--uxml-rotate: #{value}", 'transform: translate(var(--uxml-tx, 0), var(--uxml-ty, 0)) rotate(var(--uxml-rotate, 0deg)) scale(var(--uxml-scale, 1))']
    when 'translate'
      tx, ty = value.split(/\s+/)
      ["--uxml-tx: #{tx}", "--uxml-ty: #{ty || 0}", 'transform: translate(var(--uxml-tx, 0), var(--uxml-ty, 0)) rotate(var(--uxml-rotate, 0deg)) scale(var(--uxml-scale, 1))']
    when 'scale'
      ["--uxml-scale: #{value}", 'transform: translate(var(--uxml-tx, 0), var(--uxml-ty, 0)) rotate(var(--uxml-rotate, 0deg)) scale(var(--uxml-scale, 1))']
    when '-unity-font-definition', '-unity-font'
      font_family_for(value)
    when 'letter-spacing'
      # Measured: UI Toolkit feeds the px value to TextCore as character spacing in 1/100 em,
      # so "1px" renders as 0.01em. Express it the same way in CSS.
      number = value.to_f
      value.end_with?('px') ? ["letter-spacing: #{(number / 100.0).round(4)}em"] : ["letter-spacing: #{value}"]
    when 'background-image'
      value.start_with?('url(') ? ["background-image: #{value}", 'background-size: cover'] : []
    when /^-unity-/
      [] # no CSS equivalent (slice, tint, overflow clip box)
    when 'display'
      ["display: #{value == 'none' ? 'none' : 'flex'}"]
    else
      ["#{prop}: #{value}"]
    end
  end
end

class UssConverter
  private

  # url("Fonts/Barlow-Regular.ttf") -> embedded @font-face with a family named after the file.
  def font_family_for(value)
    match = value.match(/url\((['"]?)(.*?)\1\)/)
    return [] unless match
    path = File.expand_path(match[2], @uss_dir)
    return [] unless File.exist?(path)
    family = 'uxml-' + File.basename(path, File.extname(path))
    unless @font_faces.key?(family)
      mime = File.extname(path).downcase == '.otf' ? 'font/otf' : 'font/ttf'
      data = Base64.strict_encode64(File.binread(path))
      @font_faces[family] = %(@font-face { font-family: "#{family}"; src: url(data:#{mime};base64,#{data}); })
    end
    ["font-family: \"#{family}\""]
  end
end

class UxmlConverter
  def initialize(uxml_path, width, height)
    @uxml_path = uxml_path
    @width = width
    @height = height
    @uss = UssConverter.new
    @styles = []
  end

  def convert
    doc = REXML::Document.new(File.read(@uxml_path))
    root = doc.root
    body = root.elements.map { |el| render(el) }.join
    <<~HTML
      <!doctype html>
      <html><head><meta charset="utf-8"><title>UXML preview</title>
      <style>
      #{@uss.font_faces.values.join("\n")}
      #{base_css}
      #{@styles.join("\n")}
      </style></head>
      <body><div class="uxml-stage">#{body}</div></body></html>
    HTML
  end

  private

  def base_css
    <<~CSS
      html, body { margin: 0; background: #{STAGE_BACKGROUND}; }
      .uxml-stage { width: #{@width}px; height: #{@height}px; position: relative; overflow: hidden; background: #{STAGE_BACKGROUND}; }
      /* UI Toolkit defaults: every element is a flex column that shrinks, stretches its children and positions relatively. */
      /* Inherited properties (font, size, color, white-space) live on the stage only, as in USS. */
      .uxml-stage { font-family: #{DEFAULT_FONT_STACK}; font-size: #{DEFAULT_FONT_SIZE_PX}px; line-height: normal; color: #000; }
      .uxml-stage, .uxml-stage * { box-sizing: border-box; display: flex; flex-direction: column; flex-shrink: 1; align-items: stretch; position: relative;
        min-width: 0; min-height: 0; /* Yoga has no content-based automatic minimum size */
        margin: 0; padding: 0; border: 0 solid transparent; }
      uxml-label, uxml-button { white-space: pre; }
      uxml-label, uxml-button, uxml-textinput { overflow-wrap: anywhere; }
    CSS
  end

  def render(el)
    case el.name
    when 'Style'
      src = el.attributes['src']
      path = File.expand_path(src, File.dirname(@uxml_path))
      @styles << @uss.convert(File.read(path), File.dirname(path)) if File.exist?(path)
      ''
    when 'Template', 'Instance'
      '' # templates not supported in the prototype
    else
      render_element(el)
    end
  end

  def render_element(el)
    type = el.name
    tag = ELEMENT_PREFIX + type.downcase
    classes = (el.attributes['class'] || '').split(/\s+/)
    attrs = []
    attrs << %(id="#{el.attributes['name']}") if el.attributes['name']
    attrs << %(class="#{classes.join(' ')}") unless classes.empty?
    style = el.attributes['style']
    attrs << %(style="#{@uss.convert_declarations(style).strip}") if style && !style.empty?
    children = el.elements.map { |c| render(c) }.join

    case type
    when 'TextField'
      value = CGI.escapeHTML(el.attributes['value'] || '')
      # Mirrors 2022.3's TextField internals: TextInput > multiline container > TextElement with a
      # 2px right padding reserved for the caret. That padding is what makes Unity wrap 2px earlier.
      text_element = %(<uxml-textelement class="unity-text-element unity-text-element--inner-input-field-component" style="padding-right: 2px">#{value}</uxml-textelement>)
      text_element = %(<uxml-visualelement class="unity-base-text-field__multiline-container">#{text_element}</uxml-visualelement>) if el.attributes['multiline'] == 'true'
      inner = %(<uxml-textinput id="unity-text-input" class="unity-base-text-field__input unity-text-field__input">#{text_element}</uxml-textinput>)
      %(<#{tag} #{attrs.join(' ')}>#{inner}#{children}</#{tag}>)
    when 'Label', 'Button'
      text = CGI.escapeHTML(el.attributes['text'] || '')
      %(<#{tag} #{attrs.join(' ')}>#{text}#{children}</#{tag}>)
    when 'Toggle'
      label = CGI.escapeHTML(el.attributes['label'] || '')
      %(<#{tag} #{attrs.join(' ')}><uxml-visualelement class="unity-toggle__input"><uxml-visualelement class="unity-toggle__checkmark"></uxml-visualelement></uxml-visualelement><uxml-label class="unity-label">#{label}</uxml-label>#{children}</#{tag}>)
    else
      %(<#{tag} #{attrs.join(' ')}>#{children}</#{tag}>)
    end
  end
end

if __FILE__ == $PROGRAM_NAME
  input, output = ARGV[0], ARGV[1]
  abort 'usage: uxml2html.rb <file.uxml> <out.html> [--width N] [--height N]' unless input && output
  width = (ARGV.index('--width') && ARGV[ARGV.index('--width') + 1]).to_i
  height = (ARGV.index('--height') && ARGV[ARGV.index('--height') + 1]).to_i
  width = STAGE_WIDTH_DEFAULT if width <= 0
  height = STAGE_HEIGHT_DEFAULT if height <= 0
  File.write(output, UxmlConverter.new(input, width, height).convert)
  puts "wrote #{output}"
end
