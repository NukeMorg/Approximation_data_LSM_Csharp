using System;
using System.Windows;
using System.Windows.Threading;

namespace CurseWork.Views
{
    public partial class SplashScreenWindow : Window
    {
        public SplashScreenWindow()
        {
            InitializeComponent();
        }

        public static SplashScreenWindow ShowSplash()
        {
            var splash = new SplashScreenWindow();
            splash.Show();
            return splash;
        }
    }
}