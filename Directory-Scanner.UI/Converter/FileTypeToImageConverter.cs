using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using Directory_Scanner.Core.FileModels;

namespace Directory_Scanner.UI.Converter;

public class FileTypeToImageConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        
        if (value is FileType type)
        {
            string uri = type == FileType.Directory
                ? "/Image/folder.png"
                : "/Image/file.png"; 
            BitmapImage image = new BitmapImage(new Uri(uri, UriKind.Relative));
            return image;
        }

        throw new NotSupportedException();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}