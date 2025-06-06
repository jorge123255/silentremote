using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
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
            this.AttachDevTools();
#endif
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
