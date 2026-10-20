//! NTERustLauncher - Neverness To Everness 替代启动器 (Rust 实现)
//! 
//! 功能:
//!   1. 启动 ACE-Service64.exe (反作弊服务)
//!   2. 加载 WPGameClientSDK_64.dll 并调用 loginACE
//!   3. 创建互斥体
//!   4. 以正确命令行启动 HTGame.exe (避免 "map specified on commandline" 错误)
//!   5. 无 DLL 注入
//!
//! 编译: cargo build --release
//! 运行: NTERustLauncher.exe "G:\Game\Neverness To Everness"

use std::env;
use std::ffi::OsStr;
use std::fs::{self, OpenOptions};
use std::io::Write;
use std::os::windows::ffi::OsStrExt;
use std::ptr;
use std::time::{SystemTime, UNIX_EPOCH};

// ==================== Win32 类型别名 ====================
type HANDLE = *mut std::ffi::c_void;
type HMODULE = *mut std::ffi::c_void;
type FARPROC = *mut std::ffi::c_void;
type LPCSTR = *const i8;
type LPCWSTR = *const u16;
type LPWSTR = *mut u16;
type DWORD = u32;
type BOOL = i32;

// ==================== 常量 ====================
const CREATE_NEW_CONSOLE: DWORD = 0x00000010;
const WAIT_OBJECT_0: DWORD = 0;
const SW_SHOW: u16 = 5;

// ==================== 结构体 ====================
#[repr(C)]
struct STARTUPINFOW {
    cb: DWORD,
    lp_reserved: LPWSTR,
    lp_desktop: LPWSTR,
    lp_title: LPWSTR,
    dw_x: DWORD,
    dw_y: DWORD,
    dw_x_size: DWORD,
    dw_y_size: DWORD,
    dw_x_count_chars: DWORD,
    dw_y_count_chars: DWORD,
    dw_fill_attribute: DWORD,
    dw_flags: DWORD,
    w_show_window: u16,
    cb_reserved2: u16,
    lp_reserved2: *mut u8,
    h_std_input: HANDLE,
    h_std_output: HANDLE,
    h_std_error: HANDLE,
}

#[repr(C)]
struct PROCESS_INFORMATION {
    h_process: HANDLE,
    h_thread: HANDLE,
    dw_process_id: DWORD,
    dw_thread_id: DWORD,
}

// ==================== Win32 API FFI ====================
extern "system" {
    fn CreateProcessW(
        lp_application_name: LPCWSTR,
        lp_command_line: LPWSTR,
        lp_process_attributes: *mut std::ffi::c_void,
        lp_thread_attributes: *mut std::ffi::c_void,
        b_inherit_handles: BOOL,
        dw_creation_flags: DWORD,
        lp_environment: *mut std::ffi::c_void,
        lp_current_directory: LPCWSTR,
        lp_startup_info: *mut STARTUPINFOW,
        lp_process_information: *mut PROCESS_INFORMATION,
    ) -> BOOL;

    fn CloseHandle(h_object: HANDLE) -> BOOL;
    fn WaitForSingleObject(h_handle: HANDLE, dw_milliseconds: DWORD) -> DWORD;
    fn GetExitCodeProcess(h_process: HANDLE, lp_exit_code: *mut DWORD) -> BOOL;

    fn LoadLibraryW(lp_file_name: LPCWSTR) -> HMODULE;
    fn GetProcAddress(h_module: HMODULE, lp_proc_name: LPCSTR) -> FARPROC;
    fn FreeLibrary(h_module: HMODULE) -> BOOL;

    fn CreateMutexW(
        lp_mutex_attributes: *mut std::ffi::c_void,
        b_initial_owner: BOOL,
        lp_name: LPCWSTR,
    ) -> HANDLE;
}

// loginACE 函数指针类型: int loginACE(const char* accountId, int typeCommand, int worldId)
type LoginACEFunc = unsafe extern "C" fn(*const i8, i32, i32) -> i32;

// ==================== 工具函数 ====================
fn to_wide(s: &str) -> Vec<u16> {
    OsStr::new(s).encode_wide().chain(std::iter::once(0)).collect()
}

fn now_str() -> String {
    match SystemTime::now().duration_since(UNIX_EPOCH) {
        Ok(d) => {
            let secs = d.as_secs();
            let millis = d.subsec_millis();
            // 简单格式化为 [HH:MM:SS.mmm]
            let total_secs = secs;
            let hours = (total_secs / 3600) % 24;
            let minutes = (total_secs / 60) % 60;
            let seconds = total_secs % 60;
            format!("[{:02}:{:02}:{:02}.{:03}]", hours, minutes, seconds, millis)
        }
        Err(_) => String::from("[00:00:00.000]"),
    }
}

fn log(message: &str, log_path: &str) {
    let line = format!("{} {}\n", now_str(), message);
    print!("{}", line);
    if let Ok(mut f) = OpenOptions::new().create(true).append(true).open(log_path) {
        let _ = f.write_all(line.as_bytes());
    }
}

// ==================== 主逻辑 ====================
fn main() {
    let args: Vec<String> = env::args().collect();

    // 游戏根目录：命令行参数 args[1]，默认值
    let game_root = if args.len() > 1 {
        args[1].clone()
    } else {
        r"G:\Game\Neverness To Everness".to_string()
    };

    let client_bin = format!("{}\\Client\\WindowsNoEditor\\HT\\Binaries\\Win64", game_root);
    let htgame_exe = format!("{}\\HTGame.exe", client_bin);
    let ace_service_exe = format!("{}\\AntiCheatExpert\\ACE-Service64.exe", client_bin);
    let launcher_dir = format!("{}\\NTELauncher", game_root);
    let wp_game_client_sdk_dll = format!("{}\\WPGameClientSDK_64.dll", launcher_dir);
    let launch_log = format!("{}\\LaunchAnalysis\\launch.log", game_root);

    // 确保 LaunchAnalysis 目录存在
    let _ = fs::create_dir_all(format!("{}\\LaunchAnalysis", game_root));

    log(&format!("=== NTERustLauncher 启动 ==="), &launch_log);
    log(&format!("游戏根目录: {}", game_root), &launch_log);
    log(&format!("客户端目录: {}", client_bin), &launch_log);

    // 验证 HTGame.exe 存在
    if !std::path::Path::new(&htgame_exe).exists() {
        log(&format!("[错误] HTGame.exe 不存在: {}", htgame_exe), &launch_log);
        pause();
        return;
    }

    // ==================== 步骤 1: 启动 ACE 服务 ====================
    log("[步骤 1] 检查并启动 ACE 服务...", &launch_log);
    if std::path::Path::new(&ace_service_exe).exists() {
        log(&format!("  发现 ACE-Service64.exe: {}", ace_service_exe), &launch_log);
        let ace_result = std::process::Command::new(&ace_service_exe)
            .arg("/install")
            .current_dir(format!("{}\\AntiCheatExpert", client_bin))
            .output();

        match ace_result {
            Ok(output) => {
                log(
                    &format!("  ACE-Service64.exe /install 退出码: {}", output.status.code().unwrap_or(-1)),
                    &launch_log,
                );
                if !output.stdout.is_empty() {
                    log(&format!("  输出: {}", String::from_utf8_lossy(&output.stdout).trim()), &launch_log);
                }
                if !output.stderr.is_empty() {
                    log(&format!("  错误: {}", String::from_utf8_lossy(&output.stderr).trim()), &launch_log);
                }
                if output.status.success() {
                    log("  ACE 服务启动成功", &launch_log);
                } else {
                    log("  ACE 服务启动失败", &launch_log);
                }
            }
            Err(e) => {
                log(&format!("  ACE 服务启动异常: {}", e), &launch_log);
            }
        }
    } else {
        log(&format!("  ACE-Service64.exe 不存在: {}", ace_service_exe), &launch_log);
    }

    // ==================== 步骤 2: 加载 WPGameClientSDK 并尝试 ACE 握手 ====================
    log("[步骤 2] 加载 WPGameClientSDK_64.dll...", &launch_log);
    let mut sdk_handle: HMODULE = ptr::null_mut();
    if std::path::Path::new(&wp_game_client_sdk_dll).exists() {
        let wide_dll = to_wide(&wp_game_client_sdk_dll);
        unsafe {
            sdk_handle = LoadLibraryW(wide_dll.as_ptr());
        }
        if !sdk_handle.is_null() {
            log(&format!("  WPGameClientSDK_64.dll 加载成功: {}", wp_game_client_sdk_dll), &launch_log);

            // 获取 loginACE 函数
            let login_ace_name = b"loginACE\0";
            unsafe {
                let login_ace_ptr = GetProcAddress(sdk_handle, login_ace_name.as_ptr() as LPCSTR);
                if !login_ace_ptr.is_null() {
                    log("  找到 loginACE 函数，尝试 ACE 登录...", &launch_log);
                    let login_ace: LoginACEFunc = std::mem::transmute(login_ace_ptr);
                    let result = login_ace(b"\0" as *const u8 as *const i8, 0, 0);
                    log(&format!("  loginACE 返回: {}", result), &launch_log);
                } else {
                    log("  未找到 loginACE 导出函数", &launch_log);
                }

                // 获取 logoutACE
                let logout_ace_name = b"logoutACE\0";
                let logout_ace_ptr = GetProcAddress(sdk_handle, logout_ace_name.as_ptr() as LPCSTR);
                if !logout_ace_ptr.is_null() {
                    log("  找到 logoutACE 函数", &launch_log);
                }
            }
        } else {
            log(
                &format!(
                    "  WPGameClientSDK_64.dll 加载失败，错误码: {}",
                    std::io::Error::last_os_error().raw_os_error().unwrap_or(-1)
                ),
                &launch_log,
            );
        }
    } else {
        log(&format!("  WPGameClientSDK_64.dll 不存在: {}", wp_game_client_sdk_dll), &launch_log);
    }

    // ==================== 步骤 3: 创建互斥体 ====================
    log("[步骤 3] 创建互斥体...", &launch_log);
    let mutex_names = [
        "WMGP_UPDATE_MUTEX_NAME",
        "WMGP_CLIENT_MUTEX_NAME",
        "DBWinMutex",
    ];
    for mname in &mutex_names {
        let wide_name = to_wide(mname);
        unsafe {
            let h_mutex = CreateMutexW(ptr::null_mut(), 0, wide_name.as_ptr());
            if !h_mutex.is_null() {
                log(&format!("  互斥体创建成功: {}", mname), &launch_log);
                CloseHandle(h_mutex);
            } else {
                let err = std::io::Error::last_os_error().raw_os_error().unwrap_or(-1);
                log(&format!("  互斥体创建失败: {}, 错误码: {}", mname, err), &launch_log);
            }
        }
    }

    // ==================== 步骤 4: 启动 HTGame.exe ====================
    log("[步骤 4] 启动 HTGame.exe...", &launch_log);

    let map_arg = "\"/Game/LoginAndCreate/Map/Updater/Updater_P\"";
    let save_win_pos = "-SAVEWINPOS=1";
    let command_line = format!("{} {}", map_arg, save_win_pos);

    log(&format!("  命令行: {}", command_line), &launch_log);
    log(&format!("  工作目录: {}", client_bin), &launch_log);

    let app_wide = to_wide(&htgame_exe);
    let mut cmd_wide: Vec<u16> = OsStr::new(&command_line)
        .encode_wide()
        .chain(std::iter::once(0))
        .collect();
    let dir_wide = to_wide(&client_bin);

    let mut si: STARTUPINFOW = unsafe { std::mem::zeroed() };
    si.cb = std::mem::size_of::<STARTUPINFOW>() as DWORD;
    si.w_show_window = SW_SHOW;
    let mut pi: PROCESS_INFORMATION = unsafe { std::mem::zeroed() };

    let created = unsafe {
        CreateProcessW(
            app_wide.as_ptr(),
            cmd_wide.as_mut_ptr(),
            ptr::null_mut(),
            ptr::null_mut(),
            0,
            CREATE_NEW_CONSOLE,
            ptr::null_mut(),
            dir_wide.as_ptr(),
            &mut si,
            &mut pi,
        )
    };

    if created == 0 {
        let err = std::io::Error::last_os_error().raw_os_error().unwrap_or(-1);
        log(&format!("[错误] CreateProcessW 失败, 错误码: {}", err), &launch_log);
        if !sdk_handle.is_null() {
            unsafe { FreeLibrary(sdk_handle); }
        }
        pause();
        return;
    }

    log(&format!("  HTGame.exe 启动成功, PID={}", pi.dw_process_id), &launch_log);

    unsafe {
        CloseHandle(pi.h_thread);
    }

    log("  等待游戏进程...", &launch_log);
    unsafe {
        let wait_result = WaitForSingleObject(pi.h_process, 30000);
        let mut exit_code: DWORD = 0;
        GetExitCodeProcess(pi.h_process, &mut exit_code);
        CloseHandle(pi.h_process);

        if wait_result == WAIT_OBJECT_0 {
            log(&format!("  游戏进程已结束, 退出码: {}", exit_code), &launch_log);
            if exit_code == 0 {
                log("  [成功] 游戏正常启动并退出", &launch_log);
            } else {
                log("  [警告] 游戏异常退出, 可能是握手/ACE 验证失败", &launch_log);
            }
        } else {
            log("  游戏进程仍在运行 (30秒超时), 视为启动成功", &launch_log);
        }
    }

    // 清理 SDK
    if !sdk_handle.is_null() {
        unsafe { FreeLibrary(sdk_handle); }
    }

    log("=== NTERustLauncher 结束 ===", &launch_log);
    pause();
}

fn pause() {
    print!("按任意键继续...");
    let _ = std::io::stdout().flush();
    let _ = std::io::stdin().read_line(&mut String::new());
}
