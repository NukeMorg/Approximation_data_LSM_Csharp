using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CurseWork.Views
{
    public partial class HelpWindow : Window
    {
        public HelpWindow()
        {
            InitializeComponent();
            // Показываем введение при загрузке
            ShowHelp("intro");
        }

        private void HelpTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewItem item && item.Tag is string tag)
            {
                ShowHelp(tag);
            }
        }

        private void ShowHelp(string tag)
        {
            HelpContentPanel.Children.Clear();

            switch (tag)
            {
                case "intro":
                    AddTitle("Добро пожаловать в программу «Регрессионный анализ»!");
                    AddParagraph("Это приложение предназначено для построения регрессионных моделей: полиномиальной (2D) и поверхностей (3D).");
                    AddImage("Images/help_main_window.png", "Главное окно программы");
                    break;

                case "data":
                    AddTitle("Загрузка и подготовка данных");
                    AddParagraph("Программа поддерживает CSV, TXT, Excel (.xlsx) и SQLite базы данных.");
                    AddImage("Images/help_data_load.png", "Диалог выбора файла");
                    AddParagraph("Выберите нужные столбцы для осей X, Y (и Z в 3D‑режиме). Данные можно править прямо в таблице предпросмотра.");
                    break;

                case "interface":
                    AddTitle("Интерфейс программы");
                    AddParagraph("Главное окно состоит из трёх основных областей.");
                    AddImage("Images/help_main_window.png", "Общий вид главного окна");

                    AddParagraph("• Меню «Файл»: открытие данных, сохранение отчёта, экспорт графика, закрытие данных, выход.");
                    AddParagraph("• Меню «Справка»: помощь и информация о программе.");
                    AddParagraph("• Левая панель: настройки источника данных, предпросмотр, параметры модели (2D/3D).");
                    AddParagraph("• Центральная область: 2D-график или 3D-сцена с кнопками переключения режимов.");
                    AddParagraph("• Правая панель: результаты расчёта (уравнение, коэффициенты, метрики, предсказания).");
                    AddParagraph("• Строка состояния (внизу): текущий статус, индикатор загрузки и кнопка «Выход».");

                    AddTitle("Кнопка «Выход»");
                    AddParagraph("В правой части строки состояния расположена кнопка «Выход» для быстрого завершения работы приложения.");

                    AddTitle("Чекбокс «Показать сетку»");
                    AddParagraph("При работе в 2D-режиме можно включить или отключить координатную сетку на графике.");
                    break;

                case "poly":
                    AddTitle("Полиномиальная регрессия");
                    AddParagraph("Модель: y = a₀ + a₁·x + a₂·x² + ...");
                    AddImage("Images/help_2d_poly.png", "2D панель параметров");
                    break;

                case "methods":
                    AddTitle("Методы регрессии");
                    AddParagraph("• OLS – обычный метод наименьших квадратов");
                    AddParagraph("• WLS – взвешенный МНК (требуется файл весов)");
                    AddParagraph("• GLS – обобщённый МНК (требуется ковариационная матрица)");
                    AddImage("Images/help_methods.png", "Выбор метода в интерфейсе");
                    break;

                case "auto":
                    AddTitle("Автоматический подбор степени");
                    AddParagraph("При включённой опции программа перебирает степени полинома и выбирает ту, которая даёт максимальный скорректированный R².");
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
                    AddParagraph("Отчёты можно сохранить в форматах Word, Excel, текстовый, CSV или SQLite.");
                    AddImage("Images/help_save_dialog.png", "Диалог сохранения отчёта");
                    AddParagraph("Также отдельно можно экспортировать изображение графика (2D – PNG/SVG, 3D – PNG).");
                    break;

                default:
                    AddParagraph("Выберите раздел из дерева.");
                    break;
            }
        }

        // Вспомогательные методы для добавления элементов
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
                // Изображения должны быть добавлены в проект как Resource
                var uri = new Uri($"pack://application:,,,/{resourcePath}", UriKind.Absolute);
                var bitmap = new BitmapImage(uri);
                var img = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.Uniform,          // сохраняем пропорции
                    StretchDirection = StretchDirection.DownOnly, // только уменьшать, не увеличивать
                    MaxWidth = 600,
                    MaxHeight = 400,
                    Margin = new Thickness(0, 4, 0, 10)
                };
                // Дополнительно улучшим качество масштабирования
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
                // Если изображение не найдено – просто пропускаем
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