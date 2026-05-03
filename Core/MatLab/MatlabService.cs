using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using MLApp;

namespace CurseWork.Core.Matlab
{
    public class MatlabService : IDisposable
    {
        private MLApp.MLApp? _matlab;
        private readonly object _lock = new();
        private readonly Dispatcher _dispatcher;
        private readonly string _scriptsFolder;
        private int _matlabProcessId;

        public MatlabService(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _scriptsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Core", "Matlab");
        }

        private MLApp.MLApp GetMatlab()
        {
            if (_matlab == null)
            {
                lock (_lock)
                {
                    if (_matlab == null)
                    {
                        _matlab = new MLApp.MLApp();
                        _matlab.Visible = 0;
                        _matlab.Execute("set(0, 'DefaultFigureVisible', 'on');");
                        _matlab.Execute("matlab_pid = feature('getpid');");
                        object pidObj = _matlab.GetVariable("matlab_pid", "base");
                        _matlabProcessId = (pidObj is double d) ? (int)d : 0;
                    }
                }
            }
            return _matlab;
        }

        private void RunMatlabScript(MLApp.MLApp matlab, string scriptFileName)
        {
            string fullPath = Path.Combine(_scriptsFolder, scriptFileName);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"MATLAB скрипт не найден: {fullPath}");
            string script = File.ReadAllText(fullPath);
            matlab.Execute(script);
        }

        public async Task Build2DAsync(double[] x, double[] y, int degree, string method = "OLS")
        {
            var xCopy = (double[])x.Clone();
            var yCopy = (double[])y.Clone();
            int degreeCopy = degree;
            string methodCopy = method;

            await Task.Run(() =>
            {
                try
                {
                    var matlab = GetMatlab();
                    matlab.Execute("close all force;");
                    matlab.PutWorkspaceData("x", "base", xCopy);
                    matlab.PutWorkspaceData("y", "base", yCopy);
                    matlab.PutWorkspaceData("degree", "base", degreeCopy);
                    matlab.Execute($"method = '{methodCopy}';");
                    RunMatlabScript(matlab, "regress2d.m");
                }
                catch (Exception ex)
                {
                    _dispatcher.Invoke(() =>
                        MessageBox.Show($"Ошибка MATLAB (2D): {ex.Message}", "Ошибка",
                                        MessageBoxButton.OK, MessageBoxImage.Error));
                }
            });
        }

        public async Task Build3DAsync(double[] x, double[] y, double[] z, bool isPlane)
        {
            var xCopy = (double[])x.Clone();
            var yCopy = (double[])y.Clone();
            var zCopy = (double[])z.Clone();
            string surface = isPlane ? "plane" : "quadric";

            await Task.Run(() =>
            {
                try
                {
                    var matlab = GetMatlab();
                    matlab.Execute("close all force;");
                    matlab.PutWorkspaceData("x", "base", xCopy);
                    matlab.PutWorkspaceData("y", "base", yCopy);
                    matlab.PutWorkspaceData("z", "base", zCopy);
                    matlab.Execute($"surface_type = '{surface}';");
                    RunMatlabScript(matlab, "regress3d.m");
                }
                catch (Exception ex)
                {
                    _dispatcher.Invoke(() =>
                        MessageBox.Show($"Ошибка MATLAB (3D): {ex.Message}", "Ошибка",
                                        MessageBoxButton.OK, MessageBoxImage.Error));
                }
            });
        }

        public void Dispose()
        {
            if (_matlab != null)
            {
                try { _matlab.Quit(); } catch { }
                _matlab = null;
            }
            if (_matlabProcessId != 0)
            {
                try
                {
                    var proc = Process.GetProcessById(_matlabProcessId);
                    if (proc != null && proc.ProcessName.IndexOf("MATLAB", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        proc.Kill();
                        proc.WaitForExit(3000);
                    }
                }
                catch { }
            }
        }
    }
}