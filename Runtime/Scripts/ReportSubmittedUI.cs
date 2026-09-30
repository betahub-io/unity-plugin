using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BetaHub
{
    /// <summary>
    /// Post-submit step for anonymous reporters: optional email, then publish.
    /// </summary>
    public class ReportSubmittedUI : MonoBehaviour
    {
        public TMP_InputField EmailInputField;
        public Toggle EmailMyReportToggle;
        public Button SubmitEmailButton;
        public Button SkipButton;

        private ReportSubmission _submission;

        public void Show(ReportSubmission submission, string defaultEmailAddress = "")
        {
            if (submission == null)
            {
                throw new System.ArgumentNullException(nameof(submission));
            }

            Detach();
            _submission = submission;
            _submission.StateChanged += OnSubmissionStateChanged;

            EmailInputField.text = defaultEmailAddress;
            SetButtonsInteractable(true);
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        void Start()
        {
            gameObject.SetActive(false);
            SubmitEmailButton.onClick.AddListener(SubmitEmail);
            SkipButton.onClick.AddListener(Skip);
        }

        void OnDestroy()
        {
            Detach();
        }

        private void SubmitEmail()
        {
            string email = EmailInputField.text;
            if (string.IsNullOrEmpty(email))
            {
                Debug.LogWarning(UnityBetaHubLog.Prefix + "Email is empty. Enter an address or use Skip.", this);
                return;
            }

            SetButtonsInteractable(false);
            _submission.SetReporterEmail(email);
            _submission.Publish(EmailMyReportToggle.isOn);
        }

        private void Skip()
        {
            SetButtonsInteractable(false);
            _submission.Publish(false);
        }

        private void OnSubmissionStateChanged(ReportSubmission submission)
        {
            if (submission.IsFinished)
            {
                Detach();
                Hide();
            }
        }

        private void Detach()
        {
            if (_submission != null)
            {
                _submission.StateChanged -= OnSubmissionStateChanged;
                _submission = null;
            }
        }

        private void SetButtonsInteractable(bool interactable)
        {
            SubmitEmailButton.interactable = interactable;
            SkipButton.interactable = interactable;
        }
    }
}
