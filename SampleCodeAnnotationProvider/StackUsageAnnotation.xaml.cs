using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SampleCodeAnnotationProvider
{
    /// <summary>
    /// Interaction logic for StackUsageAnnotation.xaml
    /// </summary>
    public partial class StackUsageAnnotation : UserControl
    {
        public StackUsageAnnotation()
        {
            InitializeComponent();
        }

        public class Data
        {
            public Data(int depth, string text, VisualGDB.Backend.Annotations.Public.ISymbolWithReferences symbol)
            {
                GaugeWidth = depth * 5;
                if (GaugeWidth > 200)
                    GaugeWidth = 200;

                Text = text;
                CalledFunctions = symbol?.References?.Select(s => s.Name)?.ToArray();
            }

            public int GaugeWidth { get; set; }
            public string Text { get; set; }

            public string[] CalledFunctions { get; }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            //This may not get invoked due to VS internal message routing. Use Button_PreviewMouseDown() instead.
            button.IsChecked = false;
        }

        private void Button_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            button.IsChecked = false;
        }
    }
}
