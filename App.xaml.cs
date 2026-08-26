namespace PlaywrightSmartRecorder;

public partial class App : Application
{
    [Obsolete]
    public App()
    {
        InitializeComponent();
        MainPage = new MainPage();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = base.CreateWindow(activationState);

        // WinUI 3 çökmesini önlemek için boyutları native pencere oluştuktan sonra atıyoruz
        window.Created += (s, e) =>
        {
            window.Width = 1500;
            window.Height = 1050;
            window.MinimumWidth = 1200;
            window.MinimumHeight = 700;
            window.Title = "SenseWright - Playwright Smart Recorder";
        };

        return window;
    }
}