using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using VidSlice.Models;
using VidSlice.Services;
using VidSlice.ViewModels;

namespace VidSlice.Tests;

public class MainViewModelTests
{
    private const long Mb = 1024 * 1024;

    private static string NewTempFile()
    {
        var p = Path.Combine(Path.GetTempPath(), "vidslice_" + Guid.NewGuid().ToString("N") + ".mp4");
        File.WriteAllText(p, "stub");
        return p;
    }

    private static MainViewModel NewVm(
        FakeFfmpegService fake, bool ffmpegAvailable = true, AppSettings? settings = null,
        ISettingsService? settingsService = null)
        => new(fake, settingsService ?? new FakeSettingsService(settings), NullLogger<MainViewModel>.Instance, ffmpegAvailable);

    [Fact]
    public void RunAll_DisabledWhenQueueEmpty()
    {
        var vm = NewVm(new FakeFfmpegService());
        Assert.False(vm.RunAllCommand.CanExecute(null));
    }

    [Fact]
    public void RunAll_DisabledWhenFfmpegMissing()
    {
        var file = NewTempFile();
        try
        {
            var vm = NewVm(new FakeFfmpegService(), ffmpegAvailable: false);
            vm.AddFiles([file]);
            Assert.False(vm.RunAllCommand.CanExecute(null));
            Assert.Contains("ffmpeg", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void AddFiles_AddsAndAnalyzes()
    {
        var file = NewTempFile();
        try
        {
            var fake = new FakeFfmpegService();
            var vm = NewVm(fake);
            vm.AddFiles([file]);

            Assert.Single(vm.Queue);
            Assert.Equal(1, fake.ProbeCalls);
            Assert.NotNull(vm.Queue[0].MediaInfo);
            Assert.True(vm.RunAllCommand.CanExecute(null));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void AddFiles_DeduplicatesSamePath()
    {
        var file = NewTempFile();
        try
        {
            var vm = NewVm(new FakeFfmpegService());
            vm.AddFiles([file]);
            vm.AddFiles([file]);
            Assert.Single(vm.Queue);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task RunAll_ProcessesItems_PopulatesParts()
    {
        var file = NewTempFile();
        try
        {
            var parts = new List<PartResult>
            {
                new() { FileName = "a.mp4", FilePath = "a", SizeBytes = 100 * Mb, MaxBytes = 180 * Mb },
                new() { FileName = "b.mp4", FilePath = "b", SizeBytes = 120 * Mb, MaxBytes = 180 * Mb },
            };
            var fake = new FakeFfmpegService
            {
                RunResultToReturn = new RunResult { Parts = parts, AllPartsUnderLimit = true },
            };
            var vm = NewVm(fake);
            vm.AddFiles([file]);

            await vm.RunAllCommand.ExecuteAsync(null);

            Assert.Equal(1, fake.RunCalls);
            Assert.Equal(BatchStatus.Done, vm.Queue[0].Status);
            Assert.Equal(2, vm.Queue[0].Parts.Count);
            Assert.Contains("Finished", vm.StatusText);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task RunAll_BySize_PassesMaxBytes()
    {
        var file = NewTempFile();
        try
        {
            var fake = new FakeFfmpegService();
            var vm = NewVm(fake);
            vm.SelectedMode = SplitMode.BySize;
            vm.MaxSizeMb = 250;
            vm.AddFiles([file]);

            await vm.RunAllCommand.ExecuteAsync(null);

            Assert.Equal(SplitMode.BySize, fake.LastRunOptions!.Mode);
            Assert.Equal(250L * Mb, fake.LastRunOptions.MaxBytes);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task RunAll_ByParts_PassesPartCount()
    {
        var file = NewTempFile();
        try
        {
            var fake = new FakeFfmpegService();
            var vm = NewVm(fake);
            vm.SelectedMode = SplitMode.ByParts;
            vm.PartCount = 7;
            vm.AddFiles([file]);

            await vm.RunAllCommand.ExecuteAsync(null);

            Assert.Equal(SplitMode.ByParts, fake.LastRunOptions!.Mode);
            Assert.Equal(7, fake.LastRunOptions.PartCount);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task RunAll_ByDuration_ConvertsMinutesToSeconds()
    {
        var file = NewTempFile();
        try
        {
            var fake = new FakeFfmpegService();
            var vm = NewVm(fake);
            vm.SelectedMode = SplitMode.ByDuration;
            vm.SegmentMinutes = 20;
            vm.AddFiles([file]);

            await vm.RunAllCommand.ExecuteAsync(null);

            Assert.Equal(SplitMode.ByDuration, fake.LastRunOptions!.Mode);
            Assert.Equal(1200, fake.LastRunOptions.SegmentSeconds);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task RunAll_KeepOriginalFalse_DeletesInput()
    {
        var input = NewTempFile();
        var output = NewTempFile();
        try
        {
            var fake = new FakeFfmpegService
            {
                RunResultToReturn = new RunResult { Mp4Path = output, Parts = [], AllPartsUnderLimit = true },
            };
            var vm = NewVm(fake);
            vm.KeepOriginal = false;
            vm.AddFiles([input]);

            await vm.RunAllCommand.ExecuteAsync(null);

            Assert.False(File.Exists(input));
        }
        finally
        {
            if (File.Exists(input)) File.Delete(input);
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [Fact]
    public async Task RunAll_SurfacesPerItemErrors()
    {
        var file = NewTempFile();
        try
        {
            var fake = new FakeFfmpegService { RunFactory = (_, _) => throw new InvalidOperationException("boom") };
            var vm = NewVm(fake);
            vm.AddFiles([file]);

            await vm.RunAllCommand.ExecuteAsync(null);

            Assert.Equal(BatchStatus.Error, vm.Queue[0].Status);
            Assert.Contains("boom", vm.Queue[0].StatusDetail);
            Assert.False(vm.IsBusy);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Clear_EmptiesQueue()
    {
        var file = NewTempFile();
        try
        {
            var vm = NewVm(new FakeFfmpegService());
            vm.AddFiles([file]);
            Assert.True(vm.ClearCommand.CanExecute(null));

            vm.ClearCommand.Execute(null);

            Assert.Empty(vm.Queue);
            Assert.False(vm.RunAllCommand.CanExecute(null));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Settings_RoundTripThroughViewModel()
    {
        var svc = new FakeSettingsService();
        var vm = NewVm(new FakeFfmpegService(), settingsService: svc);
        vm.SelectedMode = SplitMode.ByParts;
        vm.PartCount = 9;
        vm.MaxSizeMb = 123;
        vm.Theme = "Light";

        vm.SaveSettings();

        Assert.True(svc.SaveCount > 0);
        Assert.Equal(SplitMode.ByParts, svc.Current.SplitMode);
        Assert.Equal(9, svc.Current.PartCount);
        Assert.Equal(123, svc.Current.MaxSizeMb);
        Assert.Equal("Light", svc.Current.Theme);
    }

    [Fact]
    public void Settings_LoadedIntoViewModelOnConstruction()
    {
        var settings = new AppSettings { SplitMode = SplitMode.ByDuration, SegmentMinutes = 15, Theme = "Dark" };
        var vm = NewVm(new FakeFfmpegService(), settings: settings);

        Assert.Equal(SplitMode.ByDuration, vm.SelectedMode);
        Assert.Equal(15, vm.SegmentMinutes);
        Assert.Equal("Dark", vm.Theme);
        Assert.True(vm.IsByDuration);
    }

    [Fact]
    public void PartCountValue_RoundsAndSyncsWithPartCount()
    {
        var vm = NewVm(new FakeFfmpegService());
        vm.PartCountValue = 6.7;
        Assert.Equal(7, vm.PartCount);
        Assert.Equal(7, vm.PartCountValue);

        vm.PartCount = 3;
        Assert.Equal(3, vm.PartCountValue);
    }

    [Fact]
    public void MoveUpDown_ReordersQueue()
    {
        var a = NewTempFile();
        var b = NewTempFile();
        var c = NewTempFile();
        try
        {
            var vm = NewVm(new FakeFfmpegService());
            vm.AddFiles([a, b, c]);
            var second = vm.Queue[1];

            vm.MoveUpCommand.Execute(second);
            Assert.Equal(0, vm.Queue.IndexOf(second));

            vm.MoveDownCommand.Execute(second);
            Assert.Equal(1, vm.Queue.IndexOf(second));
        }
        finally { foreach (var f in new[] { a, b, c }) File.Delete(f); }
    }

    [Fact]
    public void MoveUp_AtTop_NoOp()
    {
        var a = NewTempFile();
        var b = NewTempFile();
        try
        {
            var vm = NewVm(new FakeFfmpegService());
            vm.AddFiles([a, b]);
            var first = vm.Queue[0];
            vm.MoveUpCommand.Execute(first);
            Assert.Equal(0, vm.Queue.IndexOf(first));
        }
        finally { File.Delete(a); File.Delete(b); }
    }

    [Fact]
    public void RemoveItem_RemovesSpecificItem()
    {
        var a = NewTempFile();
        var b = NewTempFile();
        try
        {
            var vm = NewVm(new FakeFfmpegService());
            vm.AddFiles([a, b]);
            var toRemove = vm.Queue[0];

            vm.RemoveItemCommand.Execute(toRemove);

            Assert.Single(vm.Queue);
            Assert.DoesNotContain(toRemove, vm.Queue);
        }
        finally { File.Delete(a); File.Delete(b); }
    }

    [Fact]
    public async Task RetryItem_ReprocessesSingleItem()
    {
        var file = NewTempFile();
        try
        {
            var fake = new FakeFfmpegService
            {
                RunResultToReturn = new RunResult
                {
                    Parts = [new PartResult { FileName = "p.mp4", FilePath = "p", SizeBytes = 10 * Mb, MaxBytes = 180 * Mb }],
                    AllPartsUnderLimit = true,
                },
            };
            var vm = NewVm(fake);
            vm.AddFiles([file]);
            var item = vm.Queue[0];
            item.Status = BatchStatus.Error; // simulate a prior failure

            await vm.RetryItemCommand.ExecuteAsync(item);

            Assert.Equal(BatchStatus.Done, item.Status);
            Assert.Single(item.Parts);
            Assert.Equal(1, fake.RunCalls);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task RunAll_WhenAllDone_RerunsEverything()
    {
        var file = NewTempFile();
        try
        {
            var fake = new FakeFfmpegService();
            var vm = NewVm(fake);
            vm.AddFiles([file]);

            await vm.RunAllCommand.ExecuteAsync(null);
            Assert.Equal(1, fake.RunCalls);
            Assert.Equal(BatchStatus.Done, vm.Queue[0].Status);

            // Running again with everything Done should re-run.
            await vm.RunAllCommand.ExecuteAsync(null);
            Assert.Equal(2, fake.RunCalls);
        }
        finally { File.Delete(file); }
    }
}
