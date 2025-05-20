using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BetaHub
{
    public class ReportSubmittedUI : MonoBehaviour
    {
        public TMP_InputField EmailInputField;

        public Toggle EmailMyReportToggle;

        public Button SubmitEmailButton;

        public Button SkipButton;

        private Issue _issue;

        public void Show(Issue issue, string defaultEmailAddress = "")
        {
            if (issue == null)
            {
                throw new Exception("Issue is required");
            }

            _issue = issue;
            gameObject.SetActive(true);
            EmailInputField.text = defaultEmailAddress;
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }
        
        void Start()
        {
            gameObject.SetActive(false);

            SubmitEmailButton.onClick.AddListener(() => StartCoroutine(SubmitEmail()));
            SkipButton.onClick.AddListener(() => StartCoroutine(Skip()));
        }

        IEnumerator SubmitEmail()
        {
            DisableAllButtons();

            try
            {
                string email = EmailInputField.text;
                if (string.IsNullOrEmpty(email))
                {
                    Debug.LogError("Email is required");
                    yield break;
                }

                yield return _issue.SubmitEmail(email);
                yield return _issue.Publish(EmailMyReportToggle.isOn);
                Hide();
            }
            finally
            {
                EnableAllButtons();
            }
        }

        IEnumerator Skip()
        {
            DisableAllButtons();

            try
            {
                yield return _issue.Publish(false);
                Hide();
            } finally {
                EnableAllButtons();
            }
        }

        void DisableAllButtons()
        {
            SubmitEmailButton.interactable = false;
            SkipButton.interactable = false;
        }

        void EnableAllButtons()
        {
            SubmitEmailButton.interactable = true;
            SkipButton.interactable = true;
        }
    }
}