using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BetaHub
{
    public class LinkOpener : MonoBehaviour, IPointerClickHandler {
        public string Url = "https://betahub.io/terms";

        private TMP_Text _text;

        void Start()
        {
            _text = GetComponent<TMP_Text>();

            if (_text == null)
            {
                Debug.LogError("LinkOpener: No TMP_Text component found on " + gameObject.name);
            }
        }

        public void OnPointerClick(PointerEventData eventData) {
            int linkIndex = TMP_TextUtilities.FindIntersectingLink(_text, eventData.position, null);
            if (linkIndex != -1) {
                TMP_LinkInfo linkInfo = _text.textInfo.linkInfo[linkIndex];
                string linkId = linkInfo.GetLinkID();
                if (linkId == "terms")
                {
                    Application.OpenURL(Url);
                }
            }
        }
    }
}