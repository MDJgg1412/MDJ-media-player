using System;
using System.Globalization;
using System.Windows.Data;

namespace MDJMediaPlayer.Converters
{
    public sealed class KnobAngleConverter : IValueConverter
    {
        private const double MinAngle = -135d;
        private const double MaxAngle = 135d;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not double numericValue)
            {
                return MinAngle;
            }

            var maxValue = 100d;
            if (parameter != null &&
                double.TryParse(
                    parameter.ToString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var parsedMax) &&
                parsedMax > 0d)
            {
                maxValue = parsedMax;
            }

            var clamped = Math.Clamp(numericValue, 0d, maxValue);
            var ratio = clamped / maxValue;
            return MinAngle + ((MaxAngle - MinAngle) * ratio);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
