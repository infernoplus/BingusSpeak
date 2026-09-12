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
using System.Windows.Shapes;

namespace BingusSpeak
{
    public partial class NameSplitPrompt : Window
    {
        public enum NameSplitMode { None, All, Important, Unique }
        public NameSplitMode mode;

        public NameSplitPrompt(int all, int important, int unique)
        {
            mode = NameSplitMode.None;
            InitializeComponent();

            ButtonAll.Content = $"Split for all {all} NPCs";
            ButtonImportant.Content = $"Split for only {important} important NPCs";
            ButtonUnique.Content = $"Split for only {unique} unique voiced NPCs";
        }

        public void SplitNameAll_Click(object sender, RoutedEventArgs e)
        {
            mode = NameSplitMode.All;
            this.Close();
        }

        public void SplitNameImportant_Click(object sender, RoutedEventArgs e)
        {
            mode = NameSplitMode.Important;
            this.Close();
        }

        public void SplitNameUnique_Click(object sender, RoutedEventArgs e)
        {
            mode = NameSplitMode.Unique;
            this.Close();
        }

        public void SplitNameCancel_Click(object sender, RoutedEventArgs e)
        {
            mode = NameSplitMode.None;
            this.Close();
        }
    }
}
