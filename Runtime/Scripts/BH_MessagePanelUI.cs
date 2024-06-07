using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BH_MessagePanelUI : MonoBehaviour
{
    public TMP_Text messagePanelTitle;

    public TMP_Text messagePanelDescription;

    public Button messagePanelOkButton;

    private Action onCloseCallback;


    void Start()
    {
        gameObject.SetActive(false);
        messagePanelOkButton.onClick.AddListener(CloseMessagePanel);
    }

    void CloseMessagePanel()
    {
        gameObject.SetActive(false);
        onCloseCallback?.Invoke();
        onCloseCallback = null; // Clear the callback
    }

    public void ShowMessagePanel(string title, string description, Action onClose = null)
    {
        messagePanelTitle.text = title;
        messagePanelDescription.text = description;
        gameObject.SetActive(true);
        onCloseCallback = onClose;
    }
}
