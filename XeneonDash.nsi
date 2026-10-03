; XeneonDash installer (NSIS 3). Build with: makensis XeneonDash.nsi
; Expects the self-contained app in .\publish (see build.bat installer).

Unicode True
!include "MUI2.nsh"
!include "x64.nsh"
!include "LogicLib.nsh"

!getdllversion "publish\XeneonDash.exe" xdver_
!define VERSION "${xdver_1}.${xdver_2}.${xdver_3}"
!define APPNAME "XeneonDash"
!define COMPANY "Meaty Games"

Name "${APPNAME} ${VERSION}"
OutFile "XeneonDash-Setup.exe"
InstallDir "$PROGRAMFILES64\XeneonDash"
InstallDirRegKey HKLM "Software\${APPNAME}" "InstallDir"
RequestExecutionLevel admin
SetCompressor /SOLID lzma
SetCompressorDictSize 64

VIProductVersion "${xdver_1}.${xdver_2}.${xdver_3}.${xdver_4}"
VIAddVersionKey "ProductName" "${APPNAME}"
VIAddVersionKey "CompanyName" "${COMPANY}"
VIAddVersionKey "FileDescription" "${APPNAME} Setup"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "LegalCopyright" "Meaty Games"

; ---------------------------------------------------------------- UI pages
!define MUI_ICON "XeneonDash\icon.ico"
!define MUI_UNICON "XeneonDash\icon.ico"
!define MUI_WELCOMEPAGE_TEXT "This installs XeneonDash, a system-stats dashboard for the Corsair Xeneon Edge (works on any display).$\r$\n$\r$\nThe app needs administrator rights for full sensor access (GPU power/clock, drive health), so setup runs elevated and the Start-with-Windows option registers an elevated logon task — no UAC prompt at sign-in.$\r$\n$\r$\nClick Next to continue."
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!define MUI_COMPONENTSPAGE_SMALLDESC
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\XeneonDash.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Run XeneonDash now"
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

; ---------------------------------------------------------------- helpers
Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_OK|MB_ICONSTOP "XeneonDash requires 64-bit Windows."
    Abort
  ${EndIf}
  SetRegView 64
  ; Clean silent upgrade: run the previous uninstaller first if present.
  ReadRegStr $0 HKLM "Software\${APPNAME}" "InstallDir"
  ${If} $0 != ""
  ${AndIf} ${FileExists} "$0\uninstall.exe"
    ExecWait '"$0\uninstall.exe" /S _?=$0'
  ${EndIf}
FunctionEnd

Function un.onInit
  SetRegView 64
  MessageBox MB_OKCANCEL|MB_ICONQUESTION "Remove XeneonDash?$\r$\n$\r$\nYour settings (in %AppData%\XeneonDash) and CSV logs (in Documents\XeneonDash) are kept." IDOK +2
    Abort
FunctionEnd

; ---------------------------------------------------------------- sections
Section "XeneonDash" SecApp
  SectionIn RO
  SetRegView 64
  SetOutPath "$INSTDIR"

  ; Stop a running instance so files aren't locked.
  nsExec::ExecToLog 'taskkill /IM XeneonDash.exe /F'
  Sleep 500

  File /r "publish\*.*"

  ; Third-party licenses (LibreHardwareMonitor is MPL-2.0, .NET is MIT).
  SetOutPath "$INSTDIR\Licenses"
  File "packaging\licenses\LibreHardwareMonitor-MPL-2.0.txt"
  File "packaging\licenses\dotnet-MIT.txt"
  SetOutPath "$INSTDIR"

  WriteUninstaller "$INSTDIR\uninstall.exe"

  WriteRegStr HKLM "Software\${APPNAME}" "InstallDir" "$INSTDIR"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayName" "${APPNAME}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "Publisher" "${COMPANY}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayIcon" "$INSTDIR\XeneonDash.exe"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "NoModify" 1
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "NoRepair" 1
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "EstimatedSize" 167780
SectionEnd

Section "Start Menu shortcuts" SecStartMenu
  CreateDirectory "$SMPROGRAMS\${APPNAME}"
  CreateShortcut "$SMPROGRAMS\${APPNAME}\XeneonDash.lnk" "$INSTDIR\XeneonDash.exe" "" "$INSTDIR\XeneonDash.exe" 0
  CreateShortcut "$SMPROGRAMS\${APPNAME}\Uninstall XeneonDash.lnk" "$INSTDIR\uninstall.exe" "" "$INSTDIR\uninstall.exe" 0
SectionEnd

Section /o "Desktop shortcut" SecDesktop
  CreateShortcut "$DESKTOP\XeneonDash.lnk" "$INSTDIR\XeneonDash.exe" "" "$INSTDIR\XeneonDash.exe" 0
SectionEnd

Section "Start with Windows (as administrator)" SecAutoStart
  ; The app's own helper registers the elevated logon task (no 72h limit).
  ; Running elevated here, so no extra UAC prompt.
  nsExec::ExecToLog '"$INSTDIR\XeneonDash.exe" --apply-startup on'
SectionEnd

; ---------------------------------------------------------------- uninstall
Section "Uninstall"
  SetRegView 64

  nsExec::ExecToLog 'taskkill /IM XeneonDash.exe /F'
  Sleep 500
  nsExec::ExecToLog 'schtasks /Delete /TN "XeneonDash" /F'
  ; LHM's on-demand driver service (best-effort; recreated on next run).
  nsExec::ExecToLog 'sc stop R0XeneonDash'
  nsExec::ExecToLog 'sc delete R0XeneonDash'

  Delete "$SMPROGRAMS\${APPNAME}\XeneonDash.lnk"
  Delete "$SMPROGRAMS\${APPNAME}\Uninstall XeneonDash.lnk"
  RMDir "$SMPROGRAMS\${APPNAME}"
  Delete "$DESKTOP\XeneonDash.lnk"

  ; Everything in the install dir is ours (settings and CSVs live elsewhere).
  RMDir /r "$INSTDIR"

  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}"
  DeleteRegKey HKLM "Software\${APPNAME}"
SectionEnd

; ---------------------------------------------------------------- strings
!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecApp} "The dashboard application (self-contained, includes the .NET runtime)."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecStartMenu} "Start Menu entries for XeneonDash and its uninstaller."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} "A shortcut on your desktop."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecAutoStart} "Launch XeneonDash at sign-in with administrator rights (needed for full GPU/drive sensors). No UAC prompt at sign-in."
!insertmacro MUI_FUNCTION_DESCRIPTION_END
