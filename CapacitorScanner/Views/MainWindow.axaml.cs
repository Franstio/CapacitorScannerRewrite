using Avalonia.Controls;
using Avalonia.Threading;
using CapacitorScanner.Core.Model.LocalDb;
using CapacitorScanner.Messages;
using CommunityToolkit.Mvvm.Messaging;
using System.Threading.Tasks;

namespace CapacitorScanner.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000); ;
                Dispatcher.UIThread.Post(() =>
                {
                    this.WindowState = WindowState.FullScreen;
                    this.SystemDecorations = SystemDecorations.None;
                });
            });
            WeakReferenceMessenger.Default.Register<MainWindow, LoginMessage>(this, (w, m) =>
            {
                // Create an instance of MusicStoreWindow and set MusicStoreViewModel as its DataContext.
                if (m.IsClosing)
                    return;
                var dialog = new LoginWindow();
                // Show dialog window and reply with returned AlbumViewModel or null when the dialog is closed.
                m.Reply(dialog.ShowDialog<LoginModel?>(w));
            });
        }
    }
}