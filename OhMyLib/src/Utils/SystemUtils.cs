using System.Diagnostics;
using System.Text;
using Hardware.Info;

namespace OhMyLib.Utils;

public static class SystemUtils
{
    public static string GenSystemInfo()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"OS Version: {Environment.OSVersion}");
        sb.AppendLine($"Machine Name: {Environment.MachineName}");
        sb.AppendLine($".NET Version: {Environment.Version}");
        sb.AppendLine($"Processor Count: {Environment.ProcessorCount}");

        var hw = new HardwareInfo();
        hw.RefreshMemoryStatus();
        hw.RefreshCPUList();

        var cpu = hw.CpuList.FirstOrDefault();
        if (cpu != null)
        {
            sb.AppendLine($"CPU: {cpu.Name}");
            sb.AppendLine($"CPU Usage: {cpu.PercentProcessorTime}%");
        }

        var usedPhysicalMemory = (hw.MemoryStatus.TotalPhysical - hw.MemoryStatus.AvailablePhysical) / 1024.0 / 1024;
        var totalPhysicalMemory = hw.MemoryStatus.TotalPhysical / 1024.0 / 1024;
        sb.AppendLine($"Mem(Physical): {usedPhysicalMemory:F2} / {totalPhysicalMemory:F2} MB");
        using var proc = Process.GetCurrentProcess();
        var workingSet = proc.WorkingSet64 / 1024.0 / 1024;
        sb.AppendLine($"Mem(Working Set): {workingSet:F2} MB");
        var privateSize = proc.PrivateMemorySize64 / 1024.0 / 1024;
        sb.AppendLine($"Mem(Private): {privateSize:F2} MB");
        var gcMemory = GC.GetGCMemoryInfo();
        sb.AppendLine($"Mem(GC Heap): {gcMemory.HeapSizeBytes / 1024.0 / 1024:F2} MB");
        var managed = GC.GetTotalMemory(forceFullCollection: false) / 1024.0 / 1024;
        sb.AppendLine($"Mem(Managed): {managed:F2} MB");
        var runTime = DateTime.Now - proc.StartTime;
        sb.AppendLine($"Uptime: {runTime:g}");

        return sb.ToString();
    }
}