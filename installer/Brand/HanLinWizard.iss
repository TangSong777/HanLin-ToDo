; Shared by HanLin-IME, HanLin-Notes and HanLin-ToDo. Inno Setup 6.6 or newer.
[Setup]
WizardStyle=modern light excludelightcontrols hidebevels
WizardBackColor=#FFFCF9
WizardImageFile=Brand\HanLin-Welcome.png
WizardSmallImageFile=Brand\HanLin-Header.png
WizardSmallImageBackColor=#FFFCF9
DisableWelcomePage=no
DisableDirPage=no
DisableReadyPage=no
DisableProgramGroupPage=yes
DisableFinishedPage=no
ShowLanguageDialog=no
LanguageDetectionMethod=locale
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
Compression=lzma2
SolidCompression=yes
MinVersion=10.0
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os

[Languages]
Name: "chinese"; MessagesFile: "Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
chinese.WelcomeLabel1=安装 [name]
chinese.WelcomeLabel2={#SetupIntroZh}
chinese.WizardSelectDir=选择安装位置
chinese.SelectDirDesc=选择 [name] 的安装文件夹。
chinese.SelectDirLabel3=请选择安装位置，或点「浏览」选择文件夹。
chinese.ReadyLabel1=确认下方位置，然后点击「安装」。
chinese.FinishedHeadingLabel=安装完成
chinese.FinishedLabel=[name] 已安装。
chinese.FinishedLabelNoIcons=[name] 已安装。
chinese.ButtonBack=上一步(&B)
chinese.ButtonNext=下一步(&N)
chinese.ButtonWizardBrowse=浏览(&R)…
english.WelcomeLabel1=Install [name]
english.WelcomeLabel2={#SetupIntroEn}
english.FinishedHeadingLabel=Installation complete

[CustomMessages]
chinese.HanLinLaunch=打开 %1
english.HanLinLaunch=Open %1

[Code]
procedure InitializeWizard;
begin
  if HighContrastActive then
    Exit;
  { TColor uses BGR byte order. }
  WizardForm.WelcomeLabel1.Font.Color := $003C3C3C;
  WizardForm.WelcomeLabel2.Font.Color := $00606473;
  WizardForm.PageNameLabel.Font.Color := $003C3C3C;
  WizardForm.PageDescriptionLabel.Font.Color := $00606473;
  WizardForm.FinishedHeadingLabel.Font.Color := $003C3C3C;
  WizardForm.FinishedLabel.Font.Color := $00606473;
end;
