using System;
using System.Windows;

namespace BryanYOLO
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 设置未处理异常处理
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            DispatcherUnhandledException += OnDispatcherUnhandledException;
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            ShowErrorMessage(exception);
        }

        private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            ShowErrorMessage(e.Exception);
            e.Handled = true;
        }

        private void ShowErrorMessage(Exception exception)
        {
            MessageBox.Show(
                $"发生错误：{exception?.Message ?? "未知错误"}\n\n{exception?.StackTrace}",
                "BryanYOLO - 错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}