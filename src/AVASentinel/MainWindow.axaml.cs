using Avalonia.Controls;
using AVASentinel.Views;

namespace AVASentinel;

public partial class MainWindow : Window
{
    public MainWindow() : this([]) { }

    public MainWindow(string[] args)
    {
        InitializeComponent();
        Content = new GameCanvas(silent: args.Contains("--no-audio"));
    }
}
