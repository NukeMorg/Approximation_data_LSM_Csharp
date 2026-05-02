using System.Threading.Tasks;
using System.Windows;
using CurseWork.Views;  

namespace CurseWork
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var splash = SplashScreenWindow.ShowSplash();

            // Заставка висит минимум 2 секунды (можно изменить)
            await Task.Delay(2000);

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Loaded += (s, args) =>
            {
                splash.Close();
            };

            mainWindow.Show();
        }
    }
}