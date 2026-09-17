; PR Status Tracker installer.
;
; Driven entirely from the command line so the release workflow stays the single source of truth
; for versions and paths:
;   makensis /DVERSION=1.2.3 /DFILEVERSION=1.2.3.0 /DSOURCE=<publish dir> /DOUTFILE=<setup exe> \
;            installer.nsi
;
; A per-user install, so it needs no administrator and no UAC prompt: the application writes its
; settings, its start-at-login value and its notification registration under the same user.

!ifndef VERSION
  !error "VERSION is required"
!endif
!ifndef FILEVERSION
  !error "FILEVERSION is required"
!endif
!ifndef SOURCE
  !error "SOURCE is required"
!endif
!ifndef OUTFILE
  !error "OUTFILE is required"
!endif

!define APPNAME "PR Status Tracker"
!define PUBLISHER "Renewed Vision"
!define EXENAME "PrStatusTracker.exe"
; Matches WindowsNotifier.AppId: the identity every toast is raised under.
!define AUMID "PrStatusTracker"
!define REGKEY "Software\PrStatusTracker"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\PrStatusTracker"

!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "LogicLib.nsh"

Name "${APPNAME}"
OutFile "${OUTFILE}"
Unicode true
InstallDir "$LOCALAPPDATA\Programs\${APPNAME}"
InstallDirRegKey HKCU "${REGKEY}" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
; Without this the manifest says nothing about DPI, so Windows renders setup at 96 DPI and
; bitmap-stretches the result: blurred text on every scaled display. NSIS scales its own
; dialogs once it is declared aware.
ManifestDPIAware true

VIProductVersion "${FILEVERSION}"
VIAddVersionKey "ProductName" "${APPNAME}"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "CompanyName" "${PUBLISHER}"
VIAddVersionKey "LegalCopyright" "Copyright (c) 2026 ${PUBLISHER}"
VIAddVersionKey "FileDescription" "${APPNAME} setup"

!define MUI_ABORTWARNING
!define MUI_ICON "..\icon\AppIcon.ico"
!define MUI_UNICON "..\icon\AppIcon.ico"
!define MUI_FINISHPAGE_RUN "$INSTDIR\${EXENAME}"
!define MUI_FINISHPAGE_RUN_PARAMETERS "--tray"

; No directory page. Uninstall removes $INSTDIR whole, so a directory the user picked and already
; had something in is a directory this takes with it. A per-user tray application has one right
; place to live anyway.
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

; The executable of a running tray application is locked, so an upgrade would fail partway through
; copying and leave an install that is half of each version. Testing the application's own
; single-instance mutex only samples the moment of the test, so setup creates that mutex itself and
; holds it for the whole of the section: a launch from the Start Menu shortcut while setup runs
; finds the mutex taken and exits. The name is unprefixed, so it lives in this session's namespace,
; which is the session setup runs in.
!define ERROR_ALREADY_EXISTS 183
Var MutexHandle

!macro HoldSingleInstanceMutex
  waitloop:
    System::Call 'kernel32::CreateMutexW(i 0, i 0, t "PrStatusTracker-SingleInstance") i .r0 ?e'
    Pop $1
    ${If} $1 = ${ERROR_ALREADY_EXISTS}
      System::Call 'kernel32::CloseHandle(i r0)'
      ; A silent run has nobody to answer the box, and QuietUninstallString is exactly that
      ; run, so /SD takes it to the Abort instead of leaving it on a dialog nobody sees.
      MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION \
        "${APPNAME} is running. Quit it from the tray icon, then retry." /SD IDCANCEL IDRETRY waitloop
      DetailPrint "${APPNAME} is running. Quit it from the tray icon, then run this again."
      Abort
    ${EndIf}
    StrCpy $MutexHandle $0
!macroend

Section "Install"
  !insertmacro HoldSingleInstanceMutex

  SetOutPath "$INSTDIR"

  ; NativeAOT leaves the executable next to the Avalonia native libraries it loads at run time
  ; (Skia, HarfBuzz, ANGLE), so the whole publish directory is the payload.
  File /r /x "*.pdb" /x "*.dbg" "${SOURCE}\*.*"

  ; Action Center attributes a toast to the AUMID it was raised under and, for an application that
  ; is not packaged, reads the name and icon to show from this key. Without it the toasts render
  ; the identifier itself.
  File "/oname=AppIcon.png" "..\..\src\PrStatusTracker\Assets\github.png"
  WriteRegStr HKCU "Software\Classes\AppUserModelId\${AUMID}" "DisplayName" "${APPNAME}"
  WriteRegStr HKCU "Software\Classes\AppUserModelId\${AUMID}" "IconUri" "$INSTDIR\AppIcon.png"

  CreateDirectory "$SMPROGRAMS\${APPNAME}"
  CreateShortcut "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk" "$INSTDIR\${EXENAME}"

  WriteRegStr HKCU "${REGKEY}" "InstallDir" "$INSTDIR"
  WriteUninstaller "$INSTDIR\uninstall.exe"

  WriteRegStr HKCU "${UNINSTKEY}" "DisplayName" "${APPNAME}"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTKEY}" "Publisher" "${PUBLISHER}"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayIcon" "$INSTDIR\${EXENAME}"
  WriteRegStr HKCU "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTKEY}" "UninstallString" "$\"$INSTDIR\uninstall.exe$\""
  WriteRegStr HKCU "${UNINSTKEY}" "QuietUninstallString" "$\"$INSTDIR\uninstall.exe$\" /S"
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoRepair" 1

  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKCU "${UNINSTKEY}" "EstimatedSize" "$0"

  ; The finish page launches the application, which can only start once setup has let go.
  System::Call 'kernel32::CloseHandle(i $MutexHandle)'
SectionEnd

Section "Uninstall"
  !insertmacro HoldSingleInstanceMutex

  Delete "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk"
  RMDir "$SMPROGRAMS\${APPNAME}"

  RMDir /r "$INSTDIR"

  ; Start-at-login is the application's setting to make, not setup's, but the value it writes
  ; points at the executable just deleted. Left behind, Windows tries to launch it every login.
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "PrStatusTracker"

  DeleteRegKey HKCU "Software\Classes\AppUserModelId\${AUMID}"
  DeleteRegKey HKCU "${UNINSTKEY}"
  DeleteRegKey HKCU "${REGKEY}"

  ; The settings and notification history under %LOCALAPPDATA%\PrStatusTracker are deliberately
  ; left alone, so a reinstall comes back up where the user left it.
SectionEnd
