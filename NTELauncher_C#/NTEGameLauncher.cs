/********************************************************************************************
 * NTEGameLauncher - Neverness To Everness 替代游戏启动器
 *
 * 功能:
 *   1. 启动 ACE-Service64.exe (反作弊服务, 尝试兼容 ACE)
 *   2. 以正确命令行启动 HTGame.exe (避免 "map specified on commandline" 错误)
 *   3. 加载 WPGameClientSDK_64.dll 并尝试 ACE 握手
 *   4. 支持 DLL 注入 (若 ACE 兼容失败)
 *   5. 所有输出文件放在 LaunchAnalysis 目录
 *
 * 编译: csc.exe /target:exe /out:NTEGameLauncher.exe NTEGameLauncher.cs
 *      (需要 .NET Framework 4.0+, C# 5 兼容语法)
 ********************************************************************************************/

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace NTEGameLauncher
{
    class Program
    {
        // ==================== 路径配置 ====================
        // 游戏根目录由命令行参数 args[0] 传入，未传入则使用默认值
        static string GameRoot = @"G:\Game\Neverness To Everness";
        static string ClientBin;
        static string HTGameExe;
        static string AceServiceExe;
        static string LauncherDir;
        static string WpGameClientSdkDll;
        static string LaunchLog;
        static string InjectDll;
        static string InjectSource;

        static void InitPaths(string[] args)
        {
            // 如果命令行传入了游戏路径，则使用传入的路径
            if (args != null && args.Length > 0 && !string.IsNullOrEmpty(args[0]))
            {
                GameRoot = args[0].Trim('"');
                // 去掉末尾反斜杠
                if (GameRoot.EndsWith(@"\")) GameRoot = GameRoot.Substring(0, GameRoot.Length - 1);
            }

            ClientBin = Path.Combine(GameRoot, @"Client\WindowsNoEditor\HT\Binaries\Win64");
            HTGameExe = Path.Combine(ClientBin, "HTGame.exe");
            AceServiceExe = Path.Combine(ClientBin, @"AntiCheatExpert\ACE-Service64.exe");
            LauncherDir = Path.Combine(GameRoot, "NTELauncher");
            WpGameClientSdkDll = Path.Combine(LauncherDir, "WPGameClientSDK_64.dll");
            LaunchLog = Path.Combine(GameRoot, @"LaunchAnalysis\launch.log");
            InjectDll = Path.Combine(GameRoot, @"LaunchAnalysis\NTEInject.dll");
            InjectSource = Path.Combine(GameRoot, @"LaunchAnalysis\NTEInject.cs");
        }

        // ==================== Win32 API ====================
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CreateProcessW(
            string lpApplicationName,
            string lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            [In] ref STARTUPINFOW lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern IntPtr OpenMutex(uint dwDesiredAccess, bool bInheritHandle, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReleaseMutex(IntPtr hMutex);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern IntPtr CreateMutex(IntPtr lpMutexAttributes, bool bInitialOwner, string lpName);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern bool StartService(IntPtr hService, int dwNumServiceArgs, string[] lpServiceArgVectors);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern IntPtr OpenSCManager(string lpMachineName, string lpDatabaseName, uint dwDesiredAccess);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern IntPtr OpenService(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool CloseServiceHandle(IntPtr hSCObject);

        // 互斥体常量
        const uint MUTEX_ALL_ACCESS = 0x1F0001;

        // 创建进程标志
        const uint CREATE_NEW_CONSOLE = 0x00000010;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct STARTUPINFOW
        {
            public uint cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public uint dwX;
            public uint dwY;
            public uint dwXSize;
            public uint dwYSize;
            public uint dwXCountChars;
            public uint dwYCountChars;
            public uint dwFillAttribute;
            public uint dwFlags;
            public ushort wShowWindow;
            public ushort cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public uint dwProcessId;
            public uint dwThreadId;
        }

        // ==================== WPGameClientSDK_64.dll 函数委托 ====================
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int LoginACEDelegate(string accountId, int typeCommand, int worldId);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int LogoutACEDelegate();

        // ==================== 日志 ====================
        static void Log(string message)
        {
            string line = string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}] {1}", DateTime.Now, message);
            Console.WriteLine(line);
            try
            {
                File.AppendAllText(LaunchLog, line + Environment.NewLine);
            }
            catch { }
        }

        static void Main(string[] args)
        {
            // 从命令行参数初始化路径
            InitPaths(args);

            Console.Title = "NTEGameLauncher - 替代启动器";
            Log("=== NTEGameLauncher 启动 ===");
            Log("游戏根目录: " + GameRoot);
            Log("客户端目录: " + ClientBin);

            // 验证关键文件存在
            if (!File.Exists(HTGameExe))
            {
                Log("[错误] HTGame.exe 不存在: " + HTGameExe);
                Pause();
                return;
            }

            // ==================== 步骤 1: 启动 ACE 服务 ====================
            Log("[步骤 1] 检查并启动 ACE 服务...");
            bool aceStarted = TryStartAceService();
            Log(aceStarted ? "  ACE 服务启动成功" : "  ACE 服务启动失败/跳过 (将尝试 DLL 注入)");

            // ==================== 步骤 2: 加载 WPGameClientSDK 并尝试 ACE 握手 ====================
            Log("[步骤 2] 加载 WPGameClientSDK_64.dll...");
            IntPtr sdkHandle = IntPtr.Zero;
            if (File.Exists(WpGameClientSdkDll))
            {
                sdkHandle = LoadLibrary(WpGameClientSdkDll);
                if (sdkHandle != IntPtr.Zero)
                {
                    Log("  WPGameClientSDK_64.dll 加载成功: " + WpGameClientSdkDll);

                    IntPtr loginAcePtr = GetProcAddress(sdkHandle, "loginACE");
                    if (loginAcePtr != IntPtr.Zero)
                    {
                        Log("  找到 loginACE 函数，尝试 ACE 登录...");
                        try
                        {
                            LoginACEDelegate loginAce = (LoginACEDelegate)Marshal.GetDelegateForFunctionPointer(loginAcePtr, typeof(LoginACEDelegate));
                            int result = loginAce("", 0, 0);
                            Log("  loginACE 返回: " + result);
                        }
                        catch (Exception ex)
                        {
                            Log("  loginACE 调用失败: " + ex.Message);
                        }
                    }
                    else
                    {
                        Log("  未找到 loginACE 导出函数");
                    }

                    IntPtr logoutAcePtr = GetProcAddress(sdkHandle, "logoutACE");
                    if (logoutAcePtr != IntPtr.Zero)
                    {
                        Log("  找到 logoutACE 函数");
                    }
                }
                else
                {
                    Log("  WPGameClientSDK_64.dll 加载失败，错误码: " + Marshal.GetLastWin32Error());
                }
            }
            else
            {
                Log("  WPGameClientSDK_64.dll 不存在: " + WpGameClientSdkDll);
            }

            // ==================== 步骤 3: 创建互斥体 ====================
            Log("[步骤 3] 创建互斥体...");
            string[] mutexNames = {
                "WMGP_UPDATE_MUTEX_NAME",
                "WMGP_CLIENT_MUTEX_NAME",
                "DBWinMutex"
            };
            foreach (string mname in mutexNames)
            {
                try
                {
                    IntPtr hMutex = CreateMutex(IntPtr.Zero, false, mname);
                    if (hMutex != IntPtr.Zero)
                    {
                        Log("  互斥体创建成功: " + mname);
                        CloseHandle(hMutex);
                    }
                    else
                    {
                        Log("  互斥体创建失败: " + mname + ", 错误码: " + Marshal.GetLastWin32Error());
                    }
                }
                catch (Exception ex)
                {
                    Log("  互斥体 " + mname + " 异常: " + ex.Message);
                }
            }

            // ==================== 步骤 4: 启动 HTGame.exe ====================
            Log("[步骤 4] 启动 HTGame.exe...");

            string mapArg = "\"/Game/LoginAndCreate/Map/Updater/Updater_P\"";
            string saveWinPos = "-SAVEWINPOS=1";
            string commandLine = mapArg + " " + saveWinPos;

            Log("  命令行: " + commandLine);
            Log("  工作目录: " + ClientBin);

            STARTUPINFOW si = new STARTUPINFOW();
            si.cb = (uint)Marshal.SizeOf(typeof(STARTUPINFOW));
            si.wShowWindow = 1;
            PROCESS_INFORMATION pi = new PROCESS_INFORMATION();

            bool created = CreateProcessW(
                HTGameExe,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                CREATE_NEW_CONSOLE,
                IntPtr.Zero,
                ClientBin,
                ref si,
                out pi);

            if (!created)
            {
                int err = Marshal.GetLastWin32Error();
                Log("[错误] CreateProcessW 失败, 错误码: " + err);
                if (sdkHandle != IntPtr.Zero) FreeLibrary(sdkHandle);
                Pause();
                return;
            }

            Log("  HTGame.exe 启动成功, PID=" + pi.dwProcessId);

            CloseHandle(pi.hThread);

            Log("  等待游戏进程...");
            uint waitResult = WaitForSingleObject(pi.hProcess, 30000);
            uint exitCode = 0;
            GetExitCodeProcess(pi.hProcess, out exitCode);
            CloseHandle(pi.hProcess);

            if (waitResult == 0)
            {
                Log("  游戏进程已结束, 退出码: " + exitCode);
                if (exitCode == 0)
                    Log("  [成功] 游戏正常启动并退出");
                else
                    Log("  [警告] 游戏异常退出, 可能是握手/ACE 验证失败");
            }
            else
            {
                Log("  游戏进程仍在运行 (30秒超时), 视为启动成功");
            }

            // ==================== 步骤 5: 如果游戏异常退出, 尝试 DLL 注入 ====================
            if (waitResult == 0 && exitCode != 0)
            {
                Log("[步骤 5] 游戏异常退出, 尝试 DLL 注入方案...");
                TryDllInjection();
            }

            if (sdkHandle != IntPtr.Zero) FreeLibrary(sdkHandle);

            Log("=== NTEGameLauncher 结束 ===");
            Pause();
        }

        // ==================== ACE 服务启动 ====================
        static bool TryStartAceService()
        {
            try
            {
                if (File.Exists(AceServiceExe))
                {
                    Log("  发现 ACE-Service64.exe: " + AceServiceExe);
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = AceServiceExe;
                    psi.Arguments = "/install";
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    psi.WorkingDirectory = Path.Combine(ClientBin, "AntiCheatExpert");
                    using (Process proc = Process.Start(psi))
                    {
                        string output = proc.StandardOutput.ReadToEnd();
                        string error = proc.StandardError.ReadToEnd();
                        proc.WaitForExit(5000);
                        Log("  ACE-Service64.exe /install 退出码: " + proc.ExitCode);
                        if (!string.IsNullOrEmpty(output)) Log("  输出: " + output.Trim());
                        if (!string.IsNullOrEmpty(error)) Log("  错误: " + error.Trim());
                        return proc.ExitCode == 0;
                    }
                }

                IntPtr scm = OpenSCManager(null, null, 0xF003F);
                if (scm != IntPtr.Zero)
                {
                    IntPtr svc = OpenService(scm, "ACE-Service", 0xF003F);
                    if (svc != IntPtr.Zero)
                    {
                        string[] emptyArgs = { };
                        bool started = StartService(svc, 0, emptyArgs);
                        CloseServiceHandle(svc);
                        CloseServiceHandle(scm);
                        return started;
                    }
                    CloseServiceHandle(scm);
                }
            }
            catch (Exception ex)
            {
                Log("  ACE 服务启动异常: " + ex.Message);
            }
            return false;
        }

        // ==================== DLL 注入 (备用方案) ====================
        static void TryDllInjection()
        {
            Log("  准备创建注入 DLL...");
            try
            {
                File.WriteAllText(InjectSource, InjectDllSource);
                Log("  注入 DLL 源码已写入: " + InjectSource);

                string cscPath = @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe";
                if (File.Exists(cscPath))
                {
                    string buildArgs = "/target:library /out:\"" + InjectDll + "\" \"" + InjectSource + "\"";
                    Log("  尝试编译注入 DLL: " + buildArgs);
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = cscPath;
                    psi.Arguments = buildArgs;
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.RedirectStandardOutput = true;
                    using (Process proc = Process.Start(psi))
                    {
                        string output = proc.StandardOutput.ReadToEnd();
                        proc.WaitForExit(10000);
                        if (proc.ExitCode == 0 && File.Exists(InjectDll))
                        {
                            Log("  注入 DLL 编译成功: " + InjectDll);
                        }
                        else
                        {
                            Log("  注入 DLL 编译失败: " + output);
                        }
                    }
                }
                else
                {
                    Log("  未找到 csc.exe，无法编译注入 DLL");
                }
            }
            catch (Exception ex)
            {
                Log("  DLL 注入准备失败: " + ex.Message);
            }
        }

        static void Pause()
        {
            try
            {
                Console.WriteLine("按任意键继续...");
                Console.ReadKey();
            }
            catch
            {
                // 无控制台环境下忽略
            }
        }

        // ==================== 注入 DLL 源码 (C# 模板) ====================
        static readonly string InjectDllSource =
            "using System;" + Environment.NewLine +
            "using System.Runtime.InteropServices;" + Environment.NewLine +
            "" + Environment.NewLine +
            "namespace NTEInject" + Environment.NewLine +
            "{" + Environment.NewLine +
            "    public class NTEInject" + Environment.NewLine +
            "    {" + Environment.NewLine +
            "        [DllImport(\"kernel32.dll\")]" + Environment.NewLine +
            "        static extern IntPtr LoadLibrary(string lpFileName);" + Environment.NewLine +
            "" + Environment.NewLine +
            "        public static void Initialize()" + Environment.NewLine +
            "        {" + Environment.NewLine +
            "            Console.WriteLine(\"NTEInject 已加载\");" + Environment.NewLine +
            "        }" + Environment.NewLine +
            "    }" + Environment.NewLine +
            "}";
    }
}
