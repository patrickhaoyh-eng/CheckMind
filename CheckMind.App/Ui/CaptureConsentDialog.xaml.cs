using System.Windows;

namespace CheckMind.App.Ui;

public partial class CaptureConsentDialog : Window
{
    public CaptureConsentDialog()
    {
        InitializeComponent();
        CancelButton.Click += (_, _) => { Accepted = false; Close(); };
        OkButton.Click += (_, _) => { Accepted = true; Close(); };
    }

    public bool Accepted { get; private set; }

    public bool RememberChoice => RememberCheckBox.IsChecked == true;
}

