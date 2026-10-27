using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Spark.Gbfr.Mods;

internal static class CaptainFormationCodePatch
{
    private static readonly short[] Pattern =
    [
        0x81, 0xFA, 0x89, 0x15, 0x14, 0x2A, 0x0F, 0x85, -1, -1, -1, -1,
        0x48, 0x8B, 0x0D, -1, -1, -1, -1, 0x4C, 0x8D, 0x44, 0x24, 0x20,
        0xBA, 0xB2, 0xB1, 0x26, 0x2A,
    ];
    private const int JneRelOffset = 8;
    private const int CaseBodyOffset = 12;
    private const int CaseBodyPatchLength = 7;

    public static string? Apply()
    {
        var module = Process.GetCurrentProcess().MainModule
            ?? throw new InvalidOperationException("main module unavailable");
        nint baseAddress = module.BaseAddress;
        if (FindCodeSection(baseAddress) is not var (textStart, textSize))
            return "could not read the executable's code section";

        byte[] code = new byte[textSize];
        Marshal.Copy(textStart, code, 0, textSize);

        int match = -1;
        for (int i = 0; i <= code.Length - Pattern.Length; i++)
        {
            if (!Matches(code, i))
                continue;
            if (match >= 0)
                return "pattern matched more than once";
            match = i;
        }
        if (match < 0)
            return "pattern not found (game update?)";

        int jneRel = BitConverter.ToInt32(code, match + JneRelOffset);
        int jmpRel = jneRel - 5;
        byte[] patch = [0xE9, .. BitConverter.GetBytes(jmpRel), 0x90, 0x90];

        nint site = textStart + match + CaseBodyOffset;
        if (!VirtualProtect(site, CaseBodyPatchLength, PageExecuteReadWrite, out uint oldProtect))
            return "VirtualProtect failed";
        Marshal.Copy(patch, 0, site, CaseBodyPatchLength);
        VirtualProtect(site, CaseBodyPatchLength, oldProtect, out _);
        FlushInstructionCache(GetCurrentProcess(), site, CaseBodyPatchLength);
        return null;
    }

    private static bool Matches(byte[] code, int at)
    {
        for (int j = 0; j < Pattern.Length; j++)
            if (Pattern[j] >= 0 && code[at + j] != Pattern[j])
                return false;
        return true;
    }

    private static (nint Start, int Size)? FindCodeSection(nint image)
    {
        int peOffset = Marshal.ReadInt32(image, 0x3C);
        short sectionCount = Marshal.ReadInt16(image, peOffset + 6);
        short optionalSize = Marshal.ReadInt16(image, peOffset + 20);
        nint sections = image + peOffset + 24 + optionalSize;
        for (int i = 0; i < sectionCount; i++)
        {
            nint header = sections + i * 40;
            byte[] name = new byte[8];
            Marshal.Copy(header, name, 0, 8);
            if (System.Text.Encoding.ASCII.GetString(name).TrimEnd('\0') != ".text")
                continue;
            int virtualSize = Marshal.ReadInt32(header, 8);
            int virtualAddress = Marshal.ReadInt32(header, 12);
            return (image + virtualAddress, virtualSize);
        }
        return null;
    }

    private const uint PageExecuteReadWrite = 0x40;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtect(nint address, nint size, uint newProtect, out uint oldProtect);

    [DllImport("kernel32.dll")]
    private static extern bool FlushInstructionCache(nint process, nint address, nint size);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();
}
