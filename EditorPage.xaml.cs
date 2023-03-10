// Copyright (c) Microsoft Corporation and Contributors.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Markdig;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;
using Windows.Storage;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using WinRT;
using Vitalpad;
using Microsoft.UI.Text;
using Microsoft.UI;
using Vitalpad.Utils;

namespace Vitalpad
{
    public sealed partial class EditorPage
    {
        [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Auto, PreserveSig = true, SetLastError = false)]
        private static extern IntPtr GetActiveWindow();

        private static bool IsChanged { get; set; }
        public EditorPage()
        {
            this.InitializeComponent();
        }

        private void Menu_Opening(object sender, object e)
        {
            var myFlyout = sender as CommandBarFlyout;
            if (myFlyout == null || myFlyout.Target != REBCustom) return;
            var myButton = new AppBarButton
            {
                Command = new StandardUICommand(StandardUICommandKind.Share)
            };
            myFlyout.PrimaryCommands.Add(myButton);
        }

        private void REBCustom_Loaded(object sender, RoutedEventArgs e)
        {
            REBCustom.SelectionFlyout.Opening += Menu_Opening;
            REBCustom.ContextFlyout.Opening += Menu_Opening;
        }

        private void REBCustom_Unloaded(object sender, RoutedEventArgs e)
        {
            REBCustom.SelectionFlyout.Opening -= Menu_Opening;
            REBCustom.ContextFlyout.Opening -= Menu_Opening;
        }

        private void REBCustom_TextChanged(object sender, RoutedEventArgs e)
        {
            IsChanged = true;
            REBCustom.Document.GetText(TextGetOptions.AdjustCrlf, out var value);
            SymbolsCount.Text = "Symbols count: " + value.Length;
            var delimiters = new[] { ' ', ',', '.', ';', ':', '?', '!' };
            var words = value.Split(delimiters, StringSplitOptions.RemoveEmptyEntries);
            WordsCount.Text = "Words count: " + words.Length;
        }

        private async void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            // Open a text file.
            var open = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                FileTypeFilter = { ".rtf" }
            };

            // When running on win32, FileOpenPicker needs to know the top-level hwnd via IInitializeWithWindow.Initialize.
            if (Window.Current == null)
            {
                var hwnd = GetActiveWindow();
                WinRT.Interop.InitializeWithWindow.Initialize(open, hwnd);
            }

            var file = await open.PickSingleFileAsync();

            if (file == null) return;
            using var randAccStream = await file.OpenAsync(FileAccessMode.Read);
            using var reader = new DataReader(randAccStream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)randAccStream.Size);
            var output = reader.ReadString((uint)randAccStream.Size);
            if (Helper.ActiveFiles.ContainsValue(output) && file.Name == 
                Helper.ActiveFiles.FirstOrDefault(x => x.Value == output).Key.Name)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = Content.XamlRoot,
                    Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                    Title = "Already open",
                    CloseButtonText = "Close",
                    Content = "File is already open",
                    DefaultButton = ContentDialogButton.Close
                    
                };
                await dialog.ShowAsync();
                return;
            }
            var tabViewItem = new TabViewItem
            {
                Header = file.Name,
                IconSource = new SymbolIconSource() { Symbol = Symbol.Document }
            };
            var page = new EditorPage();
            page.REBCustom.Document.LoadFromStream(TextSetOptions.FormatRtf, randAccStream);
            tabViewItem.Content = page;
            tabViewItem.IsSelected = true;
            Helper.Tabs.Add(tabViewItem);
            page.REBCustom.Document.GetText(TextGetOptions.FormatRtf, out var value);
            Helper.ActiveFiles.Add(file, value);
            Helper.Active.Add(tabViewItem, new KeyValuePair<StorageFile, string>(file, value));
        }

        private void NewFile_Click(object sender, RoutedEventArgs e)
        {
            Helper.Tabs.Add(MainWindow.CreateNewTab());
        }

        private async void SaveFile_Click(object sender, RoutedEventArgs e)
        {
            // TODO Check if current tab is attached to file => if so, change without file picker
            // REBCustom.Document.GetText(TextGetOptions.FormatRtf, out var value);
            // var fileChecked = Helper.ActiveFiles.FirstOrDefault(x => x.Value == value).Key;
            // if (Helper.ActiveFiles.ContainsKey(fileChecked))
            // {
            //     // write to file
            //     using var randAccStream = await fileChecked.OpenAsync(FileAccessMode.ReadWrite);
            //     REBCustom.Document.SaveToStream(TextGetOptions.FormatRtf, randAccStream);
            //     return;
            // }
            // Default file name if the user does not type one in or select a file to replace
            var savePicker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = "New Document"
            };

            // Dropdown of file types the user can save the file as
            savePicker.FileTypeChoices.Add("Rich Text", new List<string>() { ".rtf" });

            // When running on win32, FileSavePicker needs to know the top-level hwnd via IInitializeWithWindow::Initialize.
            if (Window.Current == null)
            {
                var hwnd = GetActiveWindow();
                WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);
            }

            IStorageFile file = await savePicker.PickSaveFileAsync();
            if (file == null) return;
            // Prevent updates to the remote version of the file until we
            // finish making changes and call CompleteUpdatesAsync.
            CachedFileManager.DeferUpdates(file);
            // write to file
            using (var randAccStream =
                   await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                REBCustom.Document.SaveToStream(TextGetOptions.FormatRtf, randAccStream);
            }

            // Let Windows know that we're finished changing the file so the
            // other app can update the remote version of the file.
            var status = await CachedFileManager.CompleteUpdatesAsync(file);
            if (status == FileUpdateStatus.Complete) return;
            var errorBox =
                new Windows.UI.Popups.MessageDialog("File " + file.Name + " couldn't be saved.");
            await errorBox.ShowAsync();

        }

        private async void AboutApp_OnClick(object sender, RoutedEventArgs e)
        {
            var info = new ContentDialog()
            {
                XamlRoot = Content.XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = "Vitalpad",
                PrimaryButtonText = "GitHub repo",
                CloseButtonText = "Close",
                Content =
                    "Small notepad app with formatting options written on C# using WinUI 3. " +
                    "\nDeveloped by Leftbrained Inc. in 2023.",
                DefaultButton = ContentDialogButton.Primary
            };
            var result = await info.ShowAsync();
            if (result == ContentDialogResult.Primary)
                await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/AmaruelleOF/Vitalpad"));
        }

        private void PasteItem_OnClick(object sender, RoutedEventArgs e)
        {
            REBCustom.Document.Selection?.Paste(0);
        }

        private void CopyItem_OnClick(object sender, RoutedEventArgs e)
        {
            REBCustom.Document.Selection?.Copy();
        }

        private void CutItem_OnClick(object sender, RoutedEventArgs e)
        {
            REBCustom.Document.Selection?.Cut();
        }

        private void UndoItem_OnClick(object sender, RoutedEventArgs e)
        {
            if (REBCustom.Document.CanUndo()) REBCustom.Document.Undo();
        }
    }
}
