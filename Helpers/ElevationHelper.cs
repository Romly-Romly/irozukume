// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Irozukume.Helpers;

// プロセスの権限(整合性レベル)に関する判定をまとめる。
// 画面ピッカーの低レベル入力フックは、自プロセスより高い整合性レベル(昇格・管理者権限)のウィンドウ宛ての入力には OS から呼ばれない(UIPI)。
// 通常権限のときに採色できないウィンドウを見分け、利用者へ示すために使う。
internal static class ElevationHelper
{
	// 整合性レベルの RID(SECURITY_MANDATORY_*_RID)。トークンの整合性 SID の末尾サブオーソリティに入る。
	private const int IntegrityUnknown = -1;
	private const int IntegrityMedium = 0x2000;

	private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
	private const uint TOKEN_QUERY = 0x0008;
	private const int TokenIntegrityLevel = 25;

	private static bool? _elevated;

	// 自プロセスが管理者権限(高整合性)で動いているか。一度判定したら変わらないのでキャッシュする。
	public static bool IsElevated => _elevated ??= ComputeElevated();




	private static bool ComputeElevated()
	{
		try
		{
			using WindowsIdentity identity = WindowsIdentity.GetCurrent();
			var principal = new WindowsPrincipal(identity);
			return principal.IsInRole(WindowsBuiltInRole.Administrator);
		}
		catch
		{
			return false;
		}
	}




	// 指定ウィンドウを所有するプロセスの整合性レベルが、自プロセスより高いか(=そこでは採色フックが効かないか)の best-effort 判定。
	// 自分が昇格済みなら、昇格ウィンドウ(高整合性)も拾えるので常に false。通常権限のときは対象の整合性を照会し、中(medium)より高ければ true。
	// 通常権限から対象のトークンを照会できない場合は、保護された高整合性プロセスの可能性が高いため true(採れない側)として扱う。
	public static bool IsWindowAboveOurIntegrity(IntPtr hwnd)
	{
		if (IsElevated || hwnd == IntPtr.Zero)
		{
			return false;
		}

		if (GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid == 0)
		{
			return false;
		}

		if (pid == (uint)Environment.ProcessId)
		{
			return false;
		}

		int level = GetProcessIntegrityLevel(pid);
		if (level == IntegrityUnknown)
		{
			return true;
		}

		return level > IntegrityMedium;
	}




	// プロセスの整合性レベル RID を返す。開けない・照会できない場合は IntegrityUnknown。
	private static int GetProcessIntegrityLevel(uint pid)
	{
		IntPtr hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
		if (hProc == IntPtr.Zero)
		{
			return IntegrityUnknown;
		}

		IntPtr hToken = IntPtr.Zero;
		IntPtr info = IntPtr.Zero;

		try
		{
			if (!OpenProcessToken(hProc, TOKEN_QUERY, out hToken))
			{
				return IntegrityUnknown;
			}

			GetTokenInformation(hToken, TokenIntegrityLevel, IntPtr.Zero, 0, out int len);
			if (len <= 0)
			{
				return IntegrityUnknown;
			}

			info = Marshal.AllocHGlobal(len);
			if (!GetTokenInformation(hToken, TokenIntegrityLevel, info, len, out _))
			{
				return IntegrityUnknown;
			}

			var label = Marshal.PtrToStructure<TOKEN_MANDATORY_LABEL>(info);
			IntPtr pSid = label.Label.Sid;
			int count = Marshal.ReadByte(GetSidSubAuthorityCount(pSid));
			IntPtr pRid = GetSidSubAuthority(pSid, (uint)(count - 1));
			return Marshal.ReadInt32(pRid);
		}
		catch
		{
			return IntegrityUnknown;
		}
		finally
		{
			if (info != IntPtr.Zero)
			{
				Marshal.FreeHGlobal(info);
			}

			if (hToken != IntPtr.Zero)
			{
				CloseHandle(hToken);
			}

			CloseHandle(hProc);
		}
	}




	[StructLayout(LayoutKind.Sequential)]
	private struct SID_AND_ATTRIBUTES
	{
		public IntPtr Sid;
		public uint Attributes;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct TOKEN_MANDATORY_LABEL
	{
		public SID_AND_ATTRIBUTES Label;
	}

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CloseHandle(IntPtr hObject);

	[DllImport("advapi32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

	[DllImport("advapi32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass, IntPtr TokenInformation, int TokenInformationLength, out int ReturnLength);

	[DllImport("advapi32.dll")]
	private static extern IntPtr GetSidSubAuthority(IntPtr pSid, uint nSubAuthority);

	[DllImport("advapi32.dll")]
	private static extern IntPtr GetSidSubAuthorityCount(IntPtr pSid);
}
