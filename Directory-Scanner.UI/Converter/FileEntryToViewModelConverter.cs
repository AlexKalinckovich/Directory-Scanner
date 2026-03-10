using System.Collections.ObjectModel;
using Directory_Scanner.Core.FileModels;
using Directory_Scanner.UI.Model;

namespace Directory_Scanner.UI.Converters;

public static class FileEntryToViewModelConverter
{
    public static ObservableCollection<FileEntryViewModel> ConvertToViewModels(FileEntry rootEntry)
    {
        ObservableCollection<FileEntryViewModel> rootItems = new ObservableCollection<FileEntryViewModel>();

        if (rootEntry == null)
        {
            return rootItems;
        }

        FileEntryViewModel rootViewModel = new FileEntryViewModel(rootEntry);

        BuildViewModelTree(rootEntry, rootViewModel);

        rootItems.Add(rootViewModel);

        return rootItems;
    }

    public static FileEntryViewModel ConvertToViewModel(FileEntry entry)
    {
        if (entry == null)
        {
            return null;
        }

        FileEntryViewModel viewModel = new FileEntryViewModel(entry);

        BuildViewModelTree(entry, viewModel);

        return viewModel;
    }

    private static void BuildViewModelTree(FileEntry model, FileEntryViewModel viewModel)
    {
        foreach (FileEntry child in model.SubDirectories)
        {
            FileEntryViewModel childViewModel = new FileEntryViewModel(child);

            viewModel.Children.Add(childViewModel);

            BuildViewModelTree(child, childViewModel);
        }
    }

    public static async Task<ObservableCollection<FileEntryViewModel>> ConvertToViewModelsAsync(
        FileEntry rootEntry,
        IProgress<double> progress = null)
    {
        ObservableCollection<FileEntryViewModel> rootItems = new ObservableCollection<FileEntryViewModel>();

        if (rootEntry == null)
        {
            return rootItems;
        }

        int totalNodes = CountNodes(rootEntry);

        ProgressCounter counter = new ProgressCounter();

        FileEntryViewModel rootViewModel = new FileEntryViewModel(rootEntry);

        await BuildViewModelTreeAsync(rootEntry, rootViewModel, totalNodes, counter, progress);

        rootItems.Add(rootViewModel);

        return rootItems;
    }

    private static int CountNodes(FileEntry entry)
    {
        int count = 1;

        foreach (FileEntry child in entry.SubDirectories)
        {
            count += CountNodes(child);
        }

        return count;
    }

    private static async Task BuildViewModelTreeAsync(
        FileEntry model,
        FileEntryViewModel viewModel,
        int totalNodes,
        ProgressCounter counter,
        IProgress<double> progress)
    {
        foreach (FileEntry child in model.SubDirectories)
        {
            FileEntryViewModel childViewModel = new FileEntryViewModel(child);

            viewModel.Children.Add(childViewModel);

            await BuildViewModelTreeAsync(child, childViewModel, totalNodes, counter, progress);

            counter.Increment();

            if (progress != null && totalNodes > 0)
            {
                double percent = (double)counter.Count / totalNodes * 100.0;
                progress.Report(percent);
            }

            await Task.Yield();
        }
    }

    private sealed class ProgressCounter
    {
        private int _count;

        public int Count => _count;

        public void Increment()
        {
            System.Threading.Interlocked.Increment(ref _count);
        }
    }
}