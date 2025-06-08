using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Diagnostics;
using SilentRemote.Server.ViewModels;

namespace SilentRemote.Server.UI
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainWindowViewModel();

#if DEBUG
            // Dev tools disabled for the presentation
#endif
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
