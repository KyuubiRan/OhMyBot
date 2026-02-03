using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Hardware.Info;

namespace OhMyLib.Utils;

public static class SystemUtils
{
    public static string GetSystemInfo()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"OS: {RuntimeInformation.OSDescription}");
        sb.AppendLine($"Machine Name: {Environment.MachineName}");
        sb.AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}");

        var hw = new HardwareInfo();
        hw.RefreshMemoryStatus();
        hw.RefreshCPUList();

        var cpu = hw.CpuList.FirstOrDefault();
        if (cpu != null)
        {
            sb.AppendLine("CPU Info:");
            sb.AppendLine($" - Name: {cpu.Name}");
            sb.AppendLine($" - Usage: {cpu.PercentProcessorTime}%");
        }

        var usedPhysicalMemory = (hw.MemoryStatus.TotalPhysical - hw.MemoryStatus.AvailablePhysical) / 1024.0 / 1024;
        var totalPhysicalMemory = hw.MemoryStatus.TotalPhysical / 1024.0 / 1024;
        sb.AppendLine("Memory Info:");
        sb.AppendLine($" - Physical: {usedPhysicalMemory:F2} / {totalPhysicalMemory:F2} MB ({usedPhysicalMemory / totalPhysicalMemory:P2})");
        using var proc = Process.GetCurrentProcess();
        var workingSet = proc.WorkingSet64 / 1024.0 / 1024;
        sb.AppendLine($" - Working Set: {workingSet:F2} MB");
        var privateSize = proc.PrivateMemorySize64 / 1024.0 / 1024;
        sb.AppendLine($" - Private: {privateSize:F2} MB");
        var gcMemory = GC.GetGCMemoryInfo();
        sb.AppendLine($" - GC Heap: {gcMemory.HeapSizeBytes / 1024.0 / 1024:F2} MB");
        var managed = GC.GetTotalMemory(forceFullCollection: false) / 1024.0 / 1024;
        sb.AppendLine($" - Managed: {managed:F2} MB");
        var runTime = DateTime.Now - proc.StartTime;
        sb.AppendLine($"Uptime: {runTime:g}");

        return sb.ToString();
    }
}