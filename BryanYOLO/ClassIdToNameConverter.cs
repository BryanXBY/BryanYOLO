using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Data;

namespace BryanYOLO.Converters
{
    public class ClassIdToNameConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 && values[0] is int classId && values[1] is ObservableCollection<string> classes)
            {
                if (classId >= 0 && classId < classes.Count)
                {
                    return classes[classId];
                }
                return $"类别{classId}";
            }
            return "未知";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}