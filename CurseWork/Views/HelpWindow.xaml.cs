using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CurseWork.Views
{
    public partial class HelpWindow : Window
    {
        public HelpWindow()
        {
            InitializeComponent();
            ShowHelp("intro");
            // Установка фокуса на дерево для навигации
            HelpTree.Focus();
        }

        private void HelpTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewItem item && item.Tag is string tag)
            {
                ShowHelp(tag);
            }
        }

        private void HelpTree_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && HelpTree.SelectedItem is TreeViewItem item && item.Tag is string)
            {
                // Enter автоматически раскрывает/сворачивает ветку, но мы хотим показать содержимое.
                // Можно оставить как есть, SelectedItemChanged уже сработает.
            }
            // Стрелки вверх/вниз работают по умолчанию.
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }

        private void ShowHelp(string tag)
        {
            HelpContentPanel.Children.Clear();

            switch (tag)
            {
                case "intro":
                    AddTitle("Добро пожаловать в программу «Регрессионный анализ»!");
                    AddParagraph("Это приложение для построения регрессионных моделей: полиномиальной (2D) и поверхностей (3D).");
                    AddParagraph("Программа разработана в рамках курсовой работы в БНТУ, факультет ИТиР.");
                    AddImage("Images/help_main_window.png", "Главное окно программы");
                    break;

                case "data":
                    AddTitle("Загрузка и подготовка данных");
                    AddParagraph("Поддерживаются форматы: CSV, TXT, Excel (.xlsx) и SQLite базы данных.");
                    AddParagraph("Выберите источник, укажите, содержит ли первая строка заголовки, и назначьте столбцы для осей X, Y (и Z для 3D).");
                    AddParagraph("После загрузки данные отображаются в таблице предпросмотра. Их можно редактировать прямо на месте – изменения можно сохранить обратно в исходный файл.");
                    AddImage("Images/help_data_load.png", "Диалог выбора файла");
                    break;

                case "interface":
                    AddTitle("Интерфейс программы");
                    AddParagraph("Главное окно состоит из трёх основных областей:");
                    AddImage("Images/help_main_window.png", "Общий вид главного окна");
                    AddParagraph("• Меню «Файл»: открытие данных, сохранение отчёта, экспорт графика, закрытие данных, выход.");
                    AddParagraph("• Меню «Справка»: помощь и информация о программе.");
                    AddParagraph("• Левая панель: настройки источника данных, предпросмотр, параметры модели (2D/3D).");
                    AddParagraph("• Центральная область: 2D-график или 3D-сцена с кнопками переключения режимов.");
                    AddParagraph("• Правая панель: результаты расчёта – уравнение, коэффициенты, метрики, предсказанные значения.");
                    AddParagraph("• Строка состояния (внизу): текущий статус, индикатор загрузки и кнопка «Выход».");
                    break;

                case "poly":
                    AddTitle("Полиномиальная регрессия");
                    AddParagraph("Модель: y = a₀ + a₁·x + a₂·x² + ...");
                    AddParagraph("Степень полинома задаётся ползунком (от 1 до 10) или вручную в текстовом поле.");
                    AddImage("Images/help_2d_poly.png", "2D панель параметров");
                    break;

                case "methods":
                    AddTitle("Методы регрессии");
                    AddParagraph("• OLS – обычный метод наименьших квадратов");
                    AddParagraph("• WLS – взвешенный МНК (дополнительно загружается файл весов)");
                    AddParagraph("• GLS – обобщённый МНК (загружается ковариационная матрица)");
                    AddImage("Images/help_methods.png", "Выбор метода в интерфейсе");
                    break;

                case "auto":
                    AddTitle("Автоматический подбор степени");
                    AddParagraph("При включённой опции программа перебирает степени от 1 до максимальной и выбирает ту, которая даёт наибольший скорректированный R².");
                    break;

                case "plane":
                    AddTitle("Плоскость (3D)");
                    AddParagraph("Уравнение: z = a·x + b·y + c");
                    AddImage("Images/help_3d_plane.png", "Пример плоскости в 3D визуализации");
                    break;

                case "quadric":
                    AddTitle("Квадрика (3D)");
                    AddParagraph("Уравнение: z = a·x² + b·y² + c·xy + d·x + e·y + f");
                    AddImage("Images/help_3d_quadric.png", "Пример квадратичной поверхности");
                    break;

                case "save":
                    AddTitle("Сохранение и экспорт результатов");
                    AddParagraph("Отчёты можно сохранить в форматах: Word, Excel, PDF, текстовый, CSV, SQLite.");
                    AddParagraph("Также доступен экспорт изображения графика (2D – PNG/SVG, 3D – PNG).");
                    AddImage("Images/help_save_dialog.png", "Диалог сохранения отчёта");
                    break;

                case "shortcuts":
                    AddTitle("Горячие клавиши");
                    AddParagraph("В главном окне доступны следующие комбинации:");
                    AddParagraph("Ctrl+O – открыть файл с данными");
                    AddParagraph("Ctrl+S – сохранить отчёт");
                    AddParagraph("Ctrl+E – экспорт графика (2D/3D)");
                    AddParagraph("F5 – построить модель (2D или 3D) в зависимости от активного режима");
                    AddParagraph("Ctrl+W – закрыть текущий набор данных");
                    AddParagraph("Ctrl+Q – выход из программы");
                    AddParagraph("Esc – закрыть диалоговые окна (Справка, О программе)");
                    AddParagraph("В 2D-режиме: двойной клик по графику сбрасывает масштаб.");
                    break;

                default:
                    AddParagraph("Выберите раздел из дерева.");
                    break;
            }
        }

        // Вспомогательные методы (без изменений)
        private void AddTitle(string text)
        {
            HelpContentPanel.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            });
        }

        private void AddParagraph(string text)
        {
            HelpContentPanel.Children.Add(new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            });
        }

        private void AddImage(string resourcePath, string description)
        {
            try
            {
                var uri = new Uri($"pack://application:,,,/{resourcePath}", UriKind.Absolute);
                var bitmap = new BitmapImage(uri);
                var img = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.Uniform,
                    StretchDirection = StretchDirection.DownOnly,
                    MaxWidth = 600,
                    MaxHeight = 400,
                    Margin = new Thickness(0, 4, 0, 10)
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                HelpContentPanel.Children.Add(img);

                if (!string.IsNullOrEmpty(description))
                {
                    HelpContentPanel.Children.Add(new TextBlock
                    {
                        Text = description,
                        FontStyle = FontStyles.Italic,
                        Foreground = Brushes.Gray,
                        Margin = new Thickness(0, 0, 0, 8)
                    });
                }
            }
            catch
            {
                HelpContentPanel.Children.Add(new TextBlock
                {
                    Text = $"[Изображение отсутствует: {description}]",
                    Foreground = Brushes.DarkGray,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 4, 0, 10)
                });
            }
        }
    }
}