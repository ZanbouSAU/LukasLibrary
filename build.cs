#!/usr/bin/env dotnet run

using System.Diagnostics;

class Program
{
    static async Task Main(string[] args)
    {
        var config = args.Any(a => a.Equals("release", StringComparison.OrdinalIgnoreCase)) 
            ? "Release" : "Debug";

        var command = args.FirstOrDefault(a => 
            a.Equals("build", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("test", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("pack", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("all", StringComparison.OrdinalIgnoreCase))?.ToLower() ?? "all";

        bool skipTest = args.Any(a => a.Equals("--skip-test", StringComparison.OrdinalIgnoreCase) 
                                   || a.Equals("-s", StringComparison.OrdinalIgnoreCase));

        var slnFiles = Directory.GetFiles(".", "*.sln");
        if (slnFiles.Length == 0)
        {
            WriteError("未找到 .sln 解决方案文件！");
            Environment.Exit(1);
        }
        var sln = slnFiles[0];

        var baseBinDir = Path.Combine("bin");
        var libDir = Path.Combine(baseBinDir, "lib");
        var xunitDir = Path.Combine(baseBinDir, "xunit");
        var packagesDir = Path.Combine(baseBinDir, "packages");

        PrintHeader(config, command, sln, skipTest, libDir, xunitDir);

        var totalTimer = Stopwatch.StartNew();

        try
        {
            if (command is "all" or "build" or "pack")
            {
                await ExecuteAsync("清理旧文件", "dotnet", "clean", sln, "--configuration", config, "-v", "minimal");
            }

            if (command is "all" or "build" or "pack")
            {
                await ExecuteAsync("强制还原 NuGet 包", "dotnet", "restore", sln, "--force");
            }

            if (command is "all" or "build" or "pack")
            {
                Directory.CreateDirectory(libDir);
                Directory.CreateDirectory(xunitDir);

                await ExecuteAsync("编译 CoreLib（库） → bin/lib", "dotnet", "build", "CoreLib/CoreLib.csproj",
                    "--configuration", config,
                    "--no-restore",
                    "-o", libDir,
                    "-p:GeneratePackageOnBuild=false",
                    "-p:IsPackable=false");

                await ExecuteAsync("编译 TestCoreLib（测试） → bin/xunit", "dotnet", "build", "TestCoreLib/TestCoreLib.csproj",
                    "--configuration", config,
                    "--no-restore",
                    "-o", xunitDir);
            }

            if ((command is "all" or "test") && !skipTest)
            {
                var testProject = Directory.GetFiles(".", "TestCoreLib.csproj", SearchOption.AllDirectories)
                                           .FirstOrDefault();

                if (testProject != null)
                {
                    await ExecuteAsync("运行单元测试", "dotnet", "test", testProject,
                        "--configuration", config,
                        "--no-build",
                        "--logger", "console;verbosity=normal");
                }
            }

            if (command == "pack")
            {
                Directory.CreateDirectory(packagesDir);
                await ExecuteAsync("打包 NuGet 包", "dotnet", "pack", "CoreLib/CoreLib.csproj",
                    "--configuration", config,
                    "--no-build",
                    "-o", packagesDir,
                    "-p:PackageReadmeFile=README.md");
            }

            totalTimer.Stop();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n全部构建成功完成！");
            Console.WriteLine($"库输出目录: {libDir}");
            Console.WriteLine($"测试输出目录: {xunitDir}");
            Console.WriteLine($"总耗时: {totalTimer.Elapsed.TotalSeconds:F2} 秒");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            WriteError($"构建失败: {ex.Message}");
            Environment.Exit(1);
        }
    }

    static async Task ExecuteAsync(string title, string command, params string[] arguments)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n{title}");
        Console.ResetColor();

        var argString = string.Join(" ", arguments);
        Console.WriteLine($"执行: {command} {argString}");

        var timer = Stopwatch.StartNew();

        var psi = new ProcessStartInfo
        {
            FileName = command,
            Arguments = argString,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;
        process.OutputDataReceived += (s, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
        process.ErrorDataReceived += (s, e) => { if (e.Data != null) Console.Error.WriteLine("ERROR: " + e.Data); };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();
        timer.Stop();

        if (process.ExitCode == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"完成 ({timer.Elapsed.TotalSeconds:F2} 秒)");
            Console.ResetColor();
        }
        else
        {
            throw new Exception($"{command} 执行失败，退出码: {process.ExitCode}");
        }
    }

    static void PrintHeader(string config, string command, string sln, bool skipTest, string libDir, string xunitDir)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                   LukasLibrary Build Tools                   ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();

        Console.WriteLine($"配置模式: {config}");
        Console.WriteLine($"执行命令: {command.ToUpper()}");
        Console.WriteLine($"解决方案: {Path.GetFileName(sln)}");
        Console.WriteLine($"库输出目录: {libDir}");
        Console.WriteLine($"测试输出目录: {xunitDir}");
        Console.WriteLine($"跳过测试: {(skipTest ? "是" : "否")}");
        Console.WriteLine($"当前时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine(new string('─', 60));
    }

    static void WriteWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"{message}");
        Console.ResetColor();
    }

    static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"{message}");
        Console.ResetColor();
    }
}