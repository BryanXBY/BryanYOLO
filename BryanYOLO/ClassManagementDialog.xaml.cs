using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace BryanYOLO.Dialogs
{
    public partial class ClassManagementDialog : Window
    {
        public ObservableCollection<ClassItem> Classes { get; set; }

        // 预定义的颜色列表，与YoloProject保持一致
        private static readonly Color[] PredefinedColors = new[]
        {
            Color.FromRgb(255, 50, 50),    // 红色
            Color.FromRgb(50, 255, 50),    // 绿色
            Color.FromRgb(50, 50, 255),    // 蓝色
            Color.FromRgb(255, 255, 50),   // 黄色
            Color.FromRgb(255, 50, 255),   // 洋红
            Color.FromRgb(50, 255, 255),   // 青色
            Color.FromRgb(255, 150, 50),   // 橙色
            Color.FromRgb(150, 50, 255),   // 紫色
            Color.FromRgb(50, 255, 150),   // 薄荷绿
            Color.FromRgb(255, 50, 150),   // 粉红
            Color.FromRgb(150, 255, 50),   // 黄绿
            Color.FromRgb(50, 150, 255),   // 天蓝
            Color.FromRgb(255, 200, 100),  // 浅橙
            Color.FromRgb(200, 100, 255),  // 淡紫
            Color.FromRgb(100, 255, 200),  // 浅绿
            Color.FromRgb(255, 100, 200),  // 浅粉
            Color.FromRgb(200, 255, 100),  // 浅黄绿
            Color.FromRgb(100, 200, 255),  // 浅蓝
            Color.FromRgb(255, 150, 150),  // 浅红
            Color.FromRgb(150, 255, 150),  // 浅绿
        };

        public ClassManagementDialog(ObservableCollection<string> existingClasses)
        {
            InitializeComponent();

            Classes = new ObservableCollection<ClassItem>();

            // 加载现有类别
            for (int i = 0; i < existingClasses.Count; i++)
            {
                Classes.Add(new ClassItem
                {
                    Index = i,
                    Name = existingClasses[i],
                    Color = GenerateColor(i)
                });
            }

            ClassesDataGrid.ItemsSource = Classes;
        }

        private Color GenerateColor(int index)
        {
            if (index < PredefinedColors.Length)
            {
                // 使用预定义颜色
                return PredefinedColors[index];
            }
            else
            {
                // 如果索引超过预定义颜色，使用算法生成
                var hue = (index * 360.0 / 20) % 360; // 假设最多20个类别
                return HsvToRgb(hue, 0.8, 0.9);
            }
        }

        private Color HsvToRgb(double hue, double saturation, double value)
        {
            int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
            double f = hue / 60 - Math.Floor(hue / 60);

            value = value * 255;
            int v = Convert.ToInt32(value);
            int p = Convert.ToInt32(value * (1 - saturation));
            int q = Convert.ToInt32(value * (1 - f * saturation));
            int t = Convert.ToInt32(value * (1 - (1 - f) * saturation));

            switch (hi)
            {
                case 0:
                    return Color.FromRgb((byte)v, (byte)t, (byte)p);
                case 1:
                    return Color.FromRgb((byte)q, (byte)v, (byte)p);
                case 2:
                    return Color.FromRgb((byte)p, (byte)v, (byte)t);
                case 3:
                    return Color.FromRgb((byte)p, (byte)q, (byte)v);
                case 4:
                    return Color.FromRgb((byte)t, (byte)p, (byte)v);
                default:
                    return Color.FromRgb((byte)v, (byte)p, (byte)q);
            }
        }

        private void OnAddClass(object sender, RoutedEventArgs e)
        {
            AddNewClass();
        }

        private void OnNewClassKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddNewClass();
            }
        }

        private void AddNewClass()
        {
            var className = NewClassTextBox.Text.Trim();
            if (string.IsNullOrEmpty(className))
            {
                MessageBox.Show("请输入类别名称", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 检查重复
            foreach (var cls in Classes)
            {
                if (cls.Name.Equals(className, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show($"类别 '{className}' 已存在", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            // 添加新类别
            var newIndex = Classes.Count;
            Classes.Add(new ClassItem
            {
                Index = newIndex,
                Name = className,
                Color = GenerateColor(newIndex)
            });

            NewClassTextBox.Clear();
            NewClassTextBox.Focus();
        }

        private void OnDeleteClass(object sender, RoutedEventArgs e)
        {
            var button = sender as System.Windows.Controls.Button;
            var classItem = button?.Tag as ClassItem;

            if (classItem != null)
            {
                var result = MessageBox.Show(
                    $"确定要删除类别 '{classItem.Name}' 吗？\n注意：删除后需要重新调整相关标注的类别ID。",
                    "确认删除",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    Classes.Remove(classItem);

                    // 重新编号
                    for (int i = 0; i < Classes.Count; i++)
                    {
                        Classes[i].Index = i;
                        Classes[i].Color = GenerateColor(i); // 重新生成颜色以保持一致性
                    }
                }
            }
        }

        private void OnOK(object sender, RoutedEventArgs e)
        {
            if (Classes.Count == 0)
            {
                var result = MessageBox.Show(
                    "没有定义任何类别，是否继续？",
                    "确认",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.No)
                    return;
            }

            DialogResult = true;
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }

    public class ClassItem : INotifyPropertyChanged
    {
        private int _index;
        private string _name;
        private Color _color;

        public int Index
        {
            get => _index;
            set { _index = value; OnPropertyChanged(); }
        }

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public Color Color
        {
            get => _color;
            set
            {
                _color = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ColorBrush));
            }
        }

        public Brush ColorBrush => new SolidColorBrush(Color);

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}