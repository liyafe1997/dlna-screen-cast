; NSIS installer for DLNA Screen Cast (classic .exe setup, alternative to MSIX).
;
; Behavior mirrors the MSIX package where it matters:
;  - Registers Windows Defender Firewall inbound allow rules (TCP + UDP) for the
;    app exe with profile=any. DLNA casting relies on the TV connecting back to
;    the local HTTP stream endpoint, and home networks are frequently classified
;    as Public, so Private-only rules would break casting (see
;    packaging/msix/AppxManifest.template.xml).
;  - Upgrade installs silently uninstall the previous version first.
;
; Compiled by packaging/scripts/build-nsis.ps1, which supplies VERSION,
; LAYOUT_DIR (self-contained publish layout) and OUTFILE.

Unicode true
ManifestDPIAware true
SetCompressor /SOLID lzma

!ifndef VERSION
  !define VERSION "1.2.0.0"
!endif
; Target architecture of the payload: "x64" or "arm64" (the installer stub
; itself stays x86 and runs everywhere through emulation).
!ifndef ARCHITECTURE
  !define ARCHITECTURE "x64"
!endif
!ifndef LAYOUT_DIR
  !define LAYOUT_DIR "..\..\out\nsis\layout\${ARCHITECTURE}"
!endif
!ifndef OUTFILE
  !define OUTFILE "..\..\out\nsis\artifacts\DLNAScreenCast_${VERSION}_${ARCHITECTURE}_Setup.exe"
!endif

; English fallback; user-visible strings use the localized $(AppName) instead.
!define PRODUCT_NAME "DLNA Screen Cast"
!define PRODUCT_PUBLISHER "DLNA Screen Cast Project"
!define APP_EXE "DLNAScreenCast.exe"
; Shared with the uninstaller and with upgrade installs; keep the name stable
; across versions or stale rules will accumulate.
!define FW_RULE_NAME "DLNA Screen Cast"
!define UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\DLNAScreenCast"
; Pre-rename installs (product was DesktopDlnaCast) registered under this key;
; upgrades must still find and remove them.
!define LEGACY_UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\DesktopDlnaCast"

OutFile "${OUTFILE}"
InstallDir "$PROGRAMFILES64\DLNA Screen Cast"
RequestExecutionLevel admin

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "x64.nsh"

VIProductVersion "${VERSION}"
VIAddVersionKey "ProductName" "${PRODUCT_NAME}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "FileDescription" "${PRODUCT_NAME} Setup"
VIAddVersionKey "CompanyName" "${PRODUCT_PUBLISHER}"
VIAddVersionKey "LegalCopyright" "${PRODUCT_PUBLISHER}"

!define MUI_ICON "DesktopDlnaCast.ico"
!define MUI_UNICON "DesktopDlnaCast.ico"

!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_FUNCTION LaunchApp

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

; Match the app's localization set where NSIS provides a language file; the first
; language is the fallback when the OS language is not in the list. NSIS does not
; currently provide Burmese, although the installed app supports my-MM.
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "TradChinese"
!insertmacro MUI_LANGUAGE "Japanese"
!insertmacro MUI_LANGUAGE "Korean"
!insertmacro MUI_LANGUAGE "French"
!insertmacro MUI_LANGUAGE "German"
!insertmacro MUI_LANGUAGE "Spanish"
!insertmacro MUI_LANGUAGE "Italian"
!insertmacro MUI_LANGUAGE "PortugueseBR"
!insertmacro MUI_LANGUAGE "Russian"
!insertmacro MUI_LANGUAGE "Arabic"
!insertmacro MUI_LANGUAGE "Hebrew"
!insertmacro MUI_LANGUAGE "Hindi"
!insertmacro MUI_LANGUAGE "Indonesian"
!insertmacro MUI_LANGUAGE "Malay"
!insertmacro MUI_LANGUAGE "Swedish"
!insertmacro MUI_LANGUAGE "Turkish"
!insertmacro MUI_LANGUAGE "Vietnamese"
!insertmacro MUI_LANGUAGE "Thai"
!insertmacro MUI_LANGUAGE "Ukrainian"
!insertmacro MUI_LANGUAGE "Finnish"
!insertmacro MUI_LANGUAGE "Danish"
!insertmacro MUI_LANGUAGE "Norwegian"
!insertmacro MUI_LANGUAGE "Greek"
!insertmacro MUI_LANGUAGE "Hungarian"
!insertmacro MUI_LANGUAGE "Polish"

; Localized app name; keep in sync with WindowTitle in
; src/DesktopDlnaCast.App/Strings/<locale>/Resources.resw.
LangString AppName ${LANG_ENGLISH} "DLNA Screen Cast"
LangString AppName ${LANG_SIMPCHINESE} "DLNA 投屏"
LangString AppName ${LANG_TRADCHINESE} "DLNA 螢幕投放"
LangString AppName ${LANG_JAPANESE} "DLNA スクリーンキャスト"
LangString AppName ${LANG_KOREAN} "DLNA 화면 캐스트"
LangString AppName ${LANG_FRENCH} "Diffusion d’écran DLNA"
LangString AppName ${LANG_GERMAN} "DLNA-Bildschirmübertragung"
LangString AppName ${LANG_SPANISH} "Transmisión de pantalla DLNA"
LangString AppName ${LANG_ITALIAN} "Trasmissione schermo DLNA"
LangString AppName ${LANG_PORTUGUESEBR} "Transmissão de Tela DLNA"
LangString AppName ${LANG_RUSSIAN} "Трансляция экрана DLNA"
LangString AppName ${LANG_ARABIC} "بث الشاشة عبر DLNA"
LangString AppName ${LANG_HEBREW} "שידור מסך DLNA"
LangString AppName ${LANG_HINDI} "DLNA स्क्रीन कास्ट"
LangString AppName ${LANG_INDONESIAN} "Transmisi Layar DLNA"
LangString AppName ${LANG_MALAY} "Pencerminan Skrin DLNA"
LangString AppName ${LANG_SWEDISH} "DLNA-skärmspegling"
LangString AppName ${LANG_TURKISH} "DLNA Ekran Yansıtma"
LangString AppName ${LANG_VIETNAMESE} "Truyền màn hình DLNA"
LangString AppName ${LANG_THAI} "ส่งหน้าจอผ่าน DLNA"
LangString AppName ${LANG_UKRAINIAN} "Трансляція екрана DLNA"
LangString AppName ${LANG_FINNISH} "DLNA-näytön suoratoisto"
LangString AppName ${LANG_DANISH} "DLNA-skærmcasting"
LangString AppName ${LANG_NORWEGIAN} "DLNA-skjermcasting"
LangString AppName ${LANG_GREEK} "Μετάδοση οθόνης DLNA"
LangString AppName ${LANG_HUNGARIAN} "DLNA képernyőközvetítés"
LangString AppName ${LANG_POLISH} "Przesyłanie ekranu DLNA"

; Uninstall-time prompt: also delete user settings (%LOCALAPPDATA%\DLNAScreenCast)?
LangString UnDeleteSettings ${LANG_ENGLISH} "Do you also want to delete your settings and configuration data?"
LangString UnDeleteSettings ${LANG_SIMPCHINESE} "是否同时删除用户配置数据？"
LangString UnDeleteSettings ${LANG_TRADCHINESE} "是否一併刪除使用者設定資料？"
LangString UnDeleteSettings ${LANG_JAPANESE} "ユーザー設定データも削除しますか？"
LangString UnDeleteSettings ${LANG_KOREAN} "사용자 설정 데이터도 함께 삭제하시겠습니까?"
LangString UnDeleteSettings ${LANG_FRENCH} "Voulez-vous également supprimer vos paramètres et données de configuration ?"
LangString UnDeleteSettings ${LANG_GERMAN} "Möchten Sie auch Ihre Einstellungen und Konfigurationsdaten löschen?"
LangString UnDeleteSettings ${LANG_SPANISH} "¿Desea eliminar también sus ajustes y datos de configuración?"
LangString UnDeleteSettings ${LANG_ITALIAN} "Eliminare anche le impostazioni e i dati di configurazione?"
LangString UnDeleteSettings ${LANG_PORTUGUESEBR} "Deseja também excluir suas configurações e dados de configuração?"
LangString UnDeleteSettings ${LANG_RUSSIAN} "Удалить также пользовательские настройки и данные конфигурации?"
LangString UnDeleteSettings ${LANG_ARABIC} "هل تريد أيضًا حذف الإعدادات وبيانات التكوين الخاصة بك؟"
LangString UnDeleteSettings ${LANG_HEBREW} "האם למחוק גם את ההגדרות ונתוני התצורה שלך?"
LangString UnDeleteSettings ${LANG_HINDI} "क्या आप अपनी सेटिंग्स और कॉन्फ़िगरेशन डेटा भी हटाना चाहते हैं?"
LangString UnDeleteSettings ${LANG_INDONESIAN} "Hapus juga pengaturan dan data konfigurasi Anda?"
LangString UnDeleteSettings ${LANG_MALAY} "Adakah anda juga mahu memadam tetapan dan data konfigurasi anda?"
LangString UnDeleteSettings ${LANG_SWEDISH} "Vill du även ta bort dina inställningar och konfigurationsdata?"
LangString UnDeleteSettings ${LANG_TURKISH} "Ayarlarınızı ve yapılandırma verilerinizi de silmek istiyor musunuz?"
LangString UnDeleteSettings ${LANG_VIETNAMESE} "Bạn có muốn xóa cả cài đặt và dữ liệu cấu hình của mình không?"
LangString UnDeleteSettings ${LANG_THAI} "คุณต้องการลบการตั้งค่าและข้อมูลการกำหนดค่าด้วยหรือไม่?"
LangString UnDeleteSettings ${LANG_UKRAINIAN} "Видалити також ваші налаштування та дані конфігурації?"
LangString UnDeleteSettings ${LANG_FINNISH} "Haluatko poistaa myös asetuksesi ja määritystietosi?"
LangString UnDeleteSettings ${LANG_DANISH} "Vil du også slette dine indstillinger og konfigurationsdata?"
LangString UnDeleteSettings ${LANG_NORWEGIAN} "Vil du også slette innstillingene og konfigurasjonsdataene dine?"
LangString UnDeleteSettings ${LANG_GREEK} "Θέλετε επίσης να διαγράψετε τις ρυθμίσεις και τα δεδομένα διαμόρφωσης;"
LangString UnDeleteSettings ${LANG_HUNGARIAN} "Törli a beállításokat és a konfigurációs adatokat is?"
LangString UnDeleteSettings ${LANG_POLISH} "Czy chcesz również usunąć ustawienia i dane konfiguracyjne?"

; Installer/uninstaller window title follows the OS language.
Name "$(AppName)"

Function .onInit
!if "${ARCHITECTURE}" == "arm64"
  ; arm64 payload must not be installed on x64 hardware (no emulation there).
  ${IfNot} ${IsNativeARM64}
    MessageBox MB_OK|MB_ICONSTOP "This package contains the ARM64 build of $(AppName) and requires Windows 11 on ARM. Please use the x64 installer instead."
    Abort
  ${EndIf}
!else
  ; The x64 payload also runs on Windows on ARM through x64 emulation.
  ${IfNot} ${RunningX64}
    MessageBox MB_OK|MB_ICONSTOP "$(AppName) requires 64-bit Windows."
    Abort
  ${EndIf}
!endif
  SetRegView 64
  ; Default to the previous install location on upgrades.
  ReadRegStr $0 HKLM "${UNINST_KEY}" "InstallLocation"
  ${If} $0 != ""
    StrCpy $INSTDIR $0
  ${EndIf}
FunctionEnd

Function un.onInit
  SetRegView 64
FunctionEnd

; Launch without inheriting the installer's admin token.
Function LaunchApp
  Exec '"$WINDIR\explorer.exe" "$INSTDIR\${APP_EXE}"'
FunctionEnd

; Silently run the uninstaller registered under the given key, in-place so
; ExecWait actually waits; it then cannot delete itself, so clean up here.
!macro RemovePreviousVersion KEY
  ReadRegStr $R0 HKLM "${KEY}" "UninstallString"
  ReadRegStr $R1 HKLM "${KEY}" "InstallLocation"
  ${If} $R0 != ""
    DetailPrint "Removing previous version..."
    ${If} ${FileExists} "$R1\Uninstall.exe"
      ExecWait '"$R1\Uninstall.exe" /S _?=$R1'
      Delete "$R1\Uninstall.exe"
      RMDir "$R1"
    ${Else}
      ExecWait '$R0 /S'
    ${EndIf}
  ${EndIf}
!macroend

Section "-Application"
  SetRegView 64
  SetShellVarContext all

  ; Stop a running instance so files can be replaced.
  nsExec::Exec '"$SYSDIR\taskkill.exe" /F /IM "${APP_EXE}"'
  Pop $0

  ; Upgrade: silently remove the previous version first (current key, then the
  ; pre-rename DesktopDlnaCast key).
  !insertmacro RemovePreviousVersion "${UNINST_KEY}"
  !insertmacro RemovePreviousVersion "${LEGACY_UNINST_KEY}"

  SetOutPath "$INSTDIR"
  File /r "${LAYOUT_DIR}\*.*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  ; Firewall: inbound allow for the app on ALL profiles (any), TCP + UDP.
  ; The HTTP stream endpoint (TCP, dynamic port) and SSDP (UDP 1900 + unicast
  ; responses) must be reachable even when Windows classifies the home network
  ; as Public. Delete first so reinstalls do not stack duplicate rules.
  DetailPrint "Registering firewall rules (all profiles)..."
  nsExec::ExecToLog '"$SYSDIR\netsh.exe" advfirewall firewall delete rule name="${FW_RULE_NAME}"'
  Pop $0
  nsExec::ExecToLog '"$SYSDIR\netsh.exe" advfirewall firewall add rule name="${FW_RULE_NAME}" dir=in action=allow program="$INSTDIR\${APP_EXE}" enable=yes profile=any protocol=TCP'
  Pop $0
  ${If} $0 != 0
    DetailPrint "WARNING: failed to add TCP firewall rule (exit $0). Casting may not work until the app is allowed through the firewall manually."
  ${EndIf}
  nsExec::ExecToLog '"$SYSDIR\netsh.exe" advfirewall firewall add rule name="${FW_RULE_NAME}" dir=in action=allow program="$INSTDIR\${APP_EXE}" enable=yes profile=any protocol=UDP'
  Pop $0
  ${If} $0 != 0
    DetailPrint "WARNING: failed to add UDP firewall rule (exit $0). Casting may not work until the app is allowed through the firewall manually."
  ${EndIf}

  ; Shortcuts carry the localized app name (icon comes from the exe). The name
  ; is persisted so the uninstaller can delete them even if the OS language
  ; changed after install.
  CreateShortcut "$SMPROGRAMS\$(AppName).lnk" "$INSTDIR\${APP_EXE}"
  CreateShortcut "$DESKTOP\$(AppName).lnk" "$INSTDIR\${APP_EXE}"

  WriteRegStr HKLM "${UNINST_KEY}" "DisplayName" "$(AppName)"
  WriteRegStr HKLM "${UNINST_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "${UNINST_KEY}" "Publisher" "${PRODUCT_PUBLISHER}"
  WriteRegStr HKLM "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UNINST_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE}"
  WriteRegStr HKLM "${UNINST_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKLM "${UNINST_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegStr HKLM "${UNINST_KEY}" "ShortcutName" "$(AppName)"
  WriteRegDWORD HKLM "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINST_KEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKLM "${UNINST_KEY}" "EstimatedSize" "$0"
SectionEnd

Section "Uninstall"
  SetRegView 64
  SetShellVarContext all

  nsExec::Exec '"$SYSDIR\taskkill.exe" /F /IM "${APP_EXE}"'
  Pop $0

  nsExec::ExecToLog '"$SYSDIR\netsh.exe" advfirewall firewall delete rule name="${FW_RULE_NAME}"'
  Pop $0

  ; Shortcuts were named in the install-time language; prefer the recorded
  ; name, fall back to the current language.
  ReadRegStr $0 HKLM "${UNINST_KEY}" "ShortcutName"
  ${If} $0 == ""
    StrCpy $0 "$(AppName)"
  ${EndIf}
  Delete "$SMPROGRAMS\$0.lnk"
  Delete "$DESKTOP\$0.lnk"
  RMDir /r "$INSTDIR"
  DeleteRegKey HKLM "${UNINST_KEY}"

  ; Optionally delete user settings (%LOCALAPPDATA%\DLNAScreenCast, see
  ; App.xaml.cs). /SD IDNO makes silent uninstalls answer "No", so the silent
  ; uninstall performed by upgrade installs never removes the configuration.
  ; $LOCALAPPDATA must be resolved in the current-user context; in the
  ; all-users context it maps to ProgramData instead.
  MessageBox MB_YESNO|MB_ICONQUESTION "$(UnDeleteSettings)" /SD IDNO IDYES un_delete_settings
  Goto un_settings_done
un_delete_settings:
  SetShellVarContext current
  RMDir /r "$LOCALAPPDATA\DLNAScreenCast"
  SetShellVarContext all
un_settings_done:
SectionEnd
