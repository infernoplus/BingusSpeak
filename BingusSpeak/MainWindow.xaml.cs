using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using static BingusSpeak.Dialog;

namespace BingusSpeak
{
    public partial class MainWindow : Window
    {

        public ESM esm;

        public List<string> customVoices;

        public ObservableCollection<DialogItem> DialogListHierarchy { get; set; }

        public MainWindow()
        {
            InitializeComponent();

            // Populate mock data with explicit text colors
            DialogListHierarchy = new ObservableCollection<DialogItem>();

            ICollectionView view = CollectionViewSource.GetDefaultView(DialogListHierarchy);
            view.GroupDescriptions.Add(new PropertyGroupDescription("FolderName"));
            DialogList.ItemsSource = view;

            customVoices = new(); // initailize empty, load when we load the morrowind.json file from the jorpob cache
        }

        private string FilePath;
        private void Load_Click(object sender, RoutedEventArgs e)
        {
            // 1. Create an instance of the file dialog
            OpenFileDialog openFileDialog = new OpenFileDialog();

            // 2. Set file filters (Optional, but recommended)
            openFileDialog.Filter = "JSON Files (*.json)|*.json|All files (*.*)|*.*";
            openFileDialog.InitialDirectory = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);

            // 3. Show the dialog and check if the user clicked "Open"
            if (openFileDialog.ShowDialog() == true)
            {
                // 4. Read the file path selected by the user
                FilePath = openFileDialog.FileName;

                try
                {
                    // Load parts of the ESM we need, basiscally a stripped down version of the ESM class from JortPob
                    esm = new ESM(FilePath);

                    // Next load our dialog replacement data if it exists already, this is changes weve made and saved in previous sessions
                    // It's in a fixed location relative to the esm json file. We are assuming you are working out fo the 'cache' folder with this tool
                    string inPath = Path.Combine(Path.GetDirectoryName(FilePath), "text", "text_replacement_data.json");
                    if (Path.Exists(inPath))
                    {
                        string jsonString = File.ReadAllText(inPath);
                        var options = new JsonSerializerOptions { IncludeFields = true, PropertyNameCaseInsensitive = true };
                        DataDialog data = JsonSerializer.Deserialize<DataDialog>(jsonString, options);

                        // And lasty, apply that loaded change data to our working esm so we can continue from where we last saved
                        foreach (DataReplacement replacement in data.replacements)
                        {
                            DialogInfoRecord dialog = esm.GetDialogInfo(replacement.id);
                            if (dialog == null)
                            {
                                MessageBox.Show($"Error loading replacement line: {replacement.replacement}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                continue;
                            }
                            dialog.replacement = replacement.replacement;
                        }
                        foreach(DataAddition addition in data.additions)
                        {
                            DialogInfoRecord info = new(
                                addition.id, addition.type, addition.speaker, addition.job, addition.faction, addition.cell, addition.rank,
                                addition.race, addition.sex, addition.playerFaction, addition.disposition, addition.playerRank, 
                                addition.filters, addition.text, addition.mp3, addition.script
                            );
                            info.replacement = addition.replacement;

                            DialogRecord topic = esm.GetTopic(addition.topic);
                            DialogInfoRecord parent = esm.GetDialogInfo(addition.id);
                            int index = topic.infos.IndexOf(parent);
                            topic.infos.Insert(index, info);
                        }
                    }

                    esm.PostProcessDialogStuff();

                    // Lastly load custom voice overrides. This file is in the JortPob project and when JortPob builds it copies this file to the cache folder.
                    // We are assuming that this program is always used in combinatino with a build JortPob project and the morrowind.json will be in the cache folder next to custom_voice_list.json
                    string customPath = Path.Combine(Path.GetDirectoryName(FilePath), "text", "custom_voice_list.json");
                    if (Path.Exists(customPath))
                    {
                        string customJson = File.ReadAllText(customPath);
                        customVoices = JsonSerializer.Deserialize<List<string>>(customJson);
                    }
                }
                catch (IOException ex)
                {
                    MessageBox.Show($"Error reading file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                UpdateList();
            }
        }

        private void UpdateList()
        {
            // Unloaded gtfo
            if (esm == null) { return; }

            var expandState = SaveExpandState();

            DialogListHierarchy.Clear();

            // generate stuff
            foreach (DialogRecord topic in esm.dialog)
            {
                if (topic.type == DialogRecord.Type.Journal) { continue; } // unused

                foreach(DialogInfoRecord dialog in topic.infos)
                {
                    if(!dialog.HasVariable()) { continue; }

                    string txtColor;
                    if(dialog.split) { txtColor = "Blue"; }
                    else if(!dialog.HasReplacement()) { txtColor = "Black"; }
                    else if (dialog.ReplacementHasVariable()) { txtColor = "Red"; }
                    else { txtColor = "Green"; }

                    string txt = dialog.replacement != null ? dialog.replacement : dialog.text;

                    DialogListHierarchy.Add(new DialogItem
                    {
                        FileName = txt.Length >= 64 ? $"{txt[..64]}..." : txt,
                        FolderName = topic.id,
                        TextColor = txtColor,
                        topic = topic,
                        dialog = dialog
                    });
                }
            }

            DialogList.UpdateLayout();
            LoadExpandState(expandState);
        }

        private HashSet<string> SaveExpandState()
        {
            // save state of collapse/expand in list so we can restore it after
            var expandedGroupNames = new HashSet<string>();
            ICollectionView view = CollectionViewSource.GetDefaultView(DialogListHierarchy);
            if (view != null && view.Groups != null)
            {
                foreach (CollectionViewGroup group in view.Groups)
                {
                    if (DialogList.ItemContainerGenerator.ContainerFromItem(group) is GroupItem groupItem)
                    {
                        Expander expander = FindVisualChild<Expander>(groupItem);

                        if (expander != null && expander.IsExpanded)
                        {
                            expandedGroupNames.Add(group.Name.ToString());
                        }
                    }
                }
            }
            return expandedGroupNames;
        }

        public void LoadExpandState(HashSet<string> expandedGroupNames)
        {
            // restore collapse/expand state
            ICollectionView view = CollectionViewSource.GetDefaultView(DialogListHierarchy);
            if (view != null && view.Groups != null)
            {
                foreach (CollectionViewGroup group in view.Groups)
                {
                    if (DialogList.ItemContainerGenerator.ContainerFromItem(group) is GroupItem groupItem)
                    {
                        Expander expander = FindVisualChild<Expander>(groupItem);
                        if (expander != null)
                        {
                            expander.IsExpanded = expandedGroupNames.Contains(group.Name.ToString());
                        }
                    }
                }
            }
        }

        private DialogItem CurrentlyEditing = null;
        private void UpdateEditor(DialogItem item)
        {
            EditorText.Text = item.dialog.GetText();
            string nfo = $"Topic - {item.topic.id}\r\n{item.dialog.used.Count()} NPCs use this line\r\n";
            if (item.dialog.speaker != null) { nfo += $"Only used by '{item.dialog.speaker}'\r\n"; }
            if (item.dialog.race != CharacterContent.Race.Any) { nfo += $"Only used by race '{item.dialog.race}'\r\n"; }
            if (item.dialog.sex != CharacterContent.Sex.Any) { nfo += $"Only used by '{item.dialog.sex}'\r\n"; }
            if (item.dialog.job != null) { nfo += $"Only used by class '{item.dialog.job}'\r\n"; }
            if (item.dialog.faction != null) { nfo += $"Only used by faction '{item.dialog.faction}'\r\n"; }
            if (item.dialog.rank >= 0) { nfo += $"Only used if npcs faction rank is '{item.dialog.rank}'\r\n"; }
            if (item.dialog.disposition > 0) { nfo += $"Only used if disposition is greater than '{item.dialog.disposition}'\r\n"; }
            if (item.dialog.cell != null) { nfo += $"Only used in location '{item.dialog.cell}'\r\n"; }
            if (item.dialog.playerFaction != null) { nfo += $"Only used if player is in faction '{item.dialog.playerFaction}'\r\n"; }
            if (item.dialog.playerRank >= 0) { nfo += $"Only used if player faction rank is '{item.dialog.playerRank}'\r\n"; }
            foreach (DialogFilter filter in item.dialog.filters)
            {
                if(string.IsNullOrEmpty(filter.id)) { nfo += $"Filter - {filter.type}::{filter.function} {filter.OperatorSymbol()} {filter.value}\r\n"; }
                else { nfo += $"Filter - {filter.type}::{filter.function}::{filter.id} {filter.OperatorSymbol()} {filter.value}\r\n"; }
                
            }
            string uz = "\r\n";
            foreach(CharacterContent content in item.dialog.used)
            {
                uz += $"{content.id}, ";
            }
            EditorInfo.Text = nfo;
            NpcInfo.Text = uz;
            CurrentlyEditing = item;
        }

        private void EditorText_LostFocus(object sender, RoutedEventArgs e)
        {
            if (CurrentlyEditing == null) { return; }
            if (EditorText.Text == CurrentlyEditing.dialog.GetText()) { return; }

            CurrentlyEditing.dialog.replacement = EditorText.Text;

            var expandState = SaveExpandState();
            string txt = CurrentlyEditing.dialog.GetText();

            CurrentlyEditing.FileName = txt.Length >= 64 ? $"{txt[..64]}..." : txt;

            string txtColor;
            if (CurrentlyEditing.dialog.split) { txtColor = "Blue"; }
            else if (!CurrentlyEditing.dialog.HasReplacement()) { txtColor = "Black"; }
            else if (CurrentlyEditing.dialog.ReplacementHasVariable()) { txtColor = "Red"; }
            else { txtColor = "Green"; }

            CurrentlyEditing.TextColor = txtColor;
            DialogList.Items.Refresh();
            DialogList.UpdateLayout();
            LoadExpandState(expandState);
        }

        private void SplitNpc_Click(object sender, RoutedEventArgs e)
        {
            if (esm == null) { return; } // guh

            DialogRecord topic = CurrentlyEditing.topic;
            DialogInfoRecord dialog = CurrentlyEditing.dialog;
            if (dialog.split)
            {
                MessageBox.Show($"Cannot split a line twice!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (dialog.used.Count() <= 1)
            {
                MessageBox.Show($"Selected dialog line is only used by one NPC, no need to split!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (
                !dialog.HasVariable(DialogInfoRecord.Variable.Name) &&
                !dialog.HasVariable(DialogInfoRecord.Variable.Class) &&
                !dialog.HasVariable(DialogInfoRecord.Variable.Race) &&
                !dialog.HasVariable(DialogInfoRecord.Variable.Cell) &&
                !dialog.HasVariable(DialogInfoRecord.Variable.Faction) &&
                !dialog.HasVariable(DialogInfoRecord.Variable.Rank)
            )
            {
                MessageBox.Show($"Selected dialog line does not have any variables that could be split per NPC!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            List<CharacterContent> allChars = new(), importantChars = new(), uniqueChars = new();
            foreach (CharacterContent content in dialog.used)
            {
                allChars.Add(content);
                if (content.important) { importantChars.Add(content); }
                if (customVoices.Contains(content.id.ToLower())) { uniqueChars.Add(content); }
            }

            // Instantiate the window
            NameSplitPrompt prompt = new(allChars.Count(), importantChars.Count(), uniqueChars.Count());
            prompt.Owner = this;

            // blocks until prompt is closed
            prompt.ShowDialog();

            // do operation or exit if none
            List<CharacterContent> chars;
            switch(prompt.mode)
            {
                case NameSplitPrompt.NameSplitMode.All:
                    chars = allChars;
                    break;
                case NameSplitPrompt.NameSplitMode.Important:
                    chars = importantChars;
                    break;
                case NameSplitPrompt.NameSplitMode.Unique:
                    chars = uniqueChars;
                    break;
                default:
                    return; // exit
            }

            // Deduplicate list
            for(int i=0;i<chars.Count();i++)
            {
                CharacterContent A = chars[i];
                for(int j=i+1;j<chars.Count();j++)
                {
                    CharacterContent B = chars[j];
                    if(A.id == B.id)
                    {
                        chars.RemoveAt(j);
                        j--;
                    }
                }
            }

            // Generate new lines for each npc in the chars list
            List<DialogInfoRecord> lines = new();
            foreach (CharacterContent content in chars)
            {
                DialogInfoRecord line = dialog.Split(esm, content);
                lines.Add(line);
            }

            // Insert these new lines directly into the dialog list in esm. placing them ABOVE the original line we split from so they take priority
            int index = topic.infos.IndexOf(dialog);
            topic.infos.InsertRange(index, lines);

            // Regenerate list in ui
            UpdateList();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (FilePath == null) { return; } // must load something before saving

            DataDialog data = new();
            foreach(DialogRecord topic in esm.dialog)
            {
                foreach(DialogInfoRecord dialog in topic.infos)
                {
                    // For new lines created by a split
                    if (dialog.split)
                    {
                        data.additions.Add(
                            new(
                                dialog.trueId, topic.id, dialog.type, dialog.speaker, dialog.job, dialog.faction, dialog.cell, dialog.rank, 
                                dialog.race, dialog.sex, dialog.playerFaction, dialog.disposition, dialog.playerRank, dialog.filters, 
                                dialog.text, dialog.replacement, dialog.mp3, dialog.script
                            )
                        );
                    }
                    // For existing lines we are changing
                    else if(dialog.HasReplacement())
                    {
                        data.replacements.Add(new DataReplacement(dialog.trueId, topic.id, dialog.text, dialog.replacement));
                    }
                }
            }

            var options = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };

            string outPath = Path.Combine(Path.GetDirectoryName(FilePath), "text", "text_replacement_data.json");
            string jsonString = JsonSerializer.Serialize(data, options);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, jsonString);
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void Collapse_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in DialogList.Items.Groups)
            {
                GroupItem groupItem = DialogList.ItemContainerGenerator.ContainerFromItem(item) as GroupItem;
                if (groupItem != null)
                {
                    Expander expander = FindVisualChild<Expander>(groupItem);
                    if (expander != null)
                    {
                        expander.IsExpanded = false;
                    }
                }
            }
        }

        private void Expand_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in DialogList.Items.Groups)
            {
                GroupItem groupItem = DialogList.ItemContainerGenerator.ContainerFromItem(item) as GroupItem;
                if (groupItem != null)
                {
                    Expander expander = FindVisualChild<Expander>(groupItem);
                    if (expander != null)
                    {
                        expander.IsExpanded = true;
                    }
                }
            }
        }

        private T FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(obj, i);
                if (child != null && child is T)
                    return (T)child;

                T childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                    return childOfChild;
            }
            return null;
        }

        private void DialogList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Check if an item was actually selected (and not deselected)
            if (e.AddedItems.Count > 0)
            {
                // Cast the selected item back to your custom FileItem class
                if (e.AddedItems[0] is DialogItem selectedFile)
                {
                    // Trigger your custom logic here!
                    UpdateEditor(selectedFile);
                }
            }
        }
    }

    public class DialogItem
    {
        public string FileName { get; set; }
        public string FolderName { get; set; }
        public string TextColor { get; set; }

        public DialogRecord topic { get; set; }
        public DialogInfoRecord dialog { get; set; }
    }

    public class DataDialog
    {
        public List<DataReplacement> replacements;
        public List<DataAddition> additions;
        public DataDialog()
        {
            replacements = new();
            additions = new();
        }
    }

    // Json serialization class for dialog text replacements
    public record DataReplacement
    (
        Int128 id, // 64 bit int unique id for a line of dialog, these are generated by the morrowind construction kit and I assume they are consistent through mods and stuff
        string topic, // id for topic this is under
        string text, // original text
        string replacement // replacement text
    );

    // Json serialization class for dialog text additions
    public record DataAddition
    (
        Int128 id, // this is the id of the dialog line this was split from. effectively it's origin/parent. when adding new dialog we insert above this id
        string topic, // id for topic this is under
        DialogRecord.Type type,
        string speaker,
        string job,
        string faction,
        string cell,
        int rank,
        CharacterContent.Race race,
        CharacterContent.Sex sex,
        string playerFaction,
        int disposition,
        int playerRank,
        List<DialogFilter> filters,
        string text,
        string replacement,
        string mp3,
        string script
    );
}