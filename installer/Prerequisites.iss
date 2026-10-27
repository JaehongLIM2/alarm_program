[Code]
type
  TVersionParts = array[0..3] of Integer;

function ParseVersion(Value: String; var Parts: TVersionParts): Boolean;
var I, P: Integer; Piece: String;
begin
  Result := False;
  for I := 0 to 3 do Parts[I] := 0;
  for I := 0 to 3 do begin
    P := Pos('.', Value);
    if P > 0 then begin Piece := Copy(Value, 1, P - 1); Delete(Value, 1, P); end
    else begin Piece := Value; Value := ''; end;
    Parts[I] := StrToIntDef(Piece, -1);
    if Parts[I] < 0 then Exit;
    if P = 0 then begin Result := I >= 2; Exit; end;
  end;
end;

function VersionAtLeast(Value, Minimum: String): Boolean;
var A, B: TVersionParts; I: Integer;
begin
  Result := False;
  if not ParseVersion(Value, A) or not ParseVersion(Minimum, B) then Exit;
  for I := 0 to 3 do begin
    if A[I] > B[I] then begin Result := True; Exit; end;
    if A[I] < B[I] then Exit;
  end;
  Result := True;
end;

function CompatibleDotNetVersion(Value: String): Boolean;
var Parts: TVersionParts;
begin
  Result := ParseVersion(Value, Parts) and (Parts[0] = 10) and (Parts[1] = 0) and
    VersionAtLeast(Value, '{#DotNetVersion}');
end;

function HasFramework(Root, Folder, RequiredFile: String): Boolean;
var Find: TFindRec; Base: String;
begin
  Result := False;
  Base := AddBackslash(Root) + Folder;
  if FindFirst(Base + '\10.0.*', Find) then begin
    try
      repeat
        if CompatibleDotNetVersion(Find.Name) and
          FileExists(Base + '\' + Find.Name + '\' + RequiredFile) then begin Result := True; Exit; end;
      until not FindNext(Find);
    finally FindClose(Find); end;
  end;
end;

function DotNetPresent: Boolean;
var Root: String;
begin
  if not RegQueryStringValue(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64', 'InstallLocation', Root) then
    Root := ExpandConstant('{commonpf64}\dotnet');
  Result := HasFramework(Root, 'shared\Microsoft.WindowsDesktop.App', 'PresentationFramework.dll') and
    HasFramework(Root, 'shared\Microsoft.NETCore.App', 'coreclr.dll') and
    HasFramework(Root, 'host\fxr', 'hostfxr.dll');
end;

function WebViewAt(Root: Integer): Boolean;
var Version: String;
begin
  Result := RegQueryStringValue(Root,
    'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) and
    VersionAtLeast(Version, '{#WebViewVersion}');
end;

function WebViewPresent: Boolean;
begin
  Result := WebViewAt(HKCU32) or WebViewAt(HKCU64) or WebViewAt(HKLM32) or WebViewAt(HKLM64);
end;

procedure CheckPayload(Name, ExpectedHash: String);
begin
  ExtractTemporaryFile(Name);
  if not SameText(GetSHA256OfFile(ExpandConstant('{tmp}\') + Name), ExpectedHash) then
    RaiseException('Bundled prerequisite verification failed: ' + Name);
end;

function SwitchPresent(Name: String): Boolean;
var I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if SameText(ParamStr(I), '/' + Name) then begin Result := True; Exit; end;
end;

function Flag(Value: Boolean): String;
begin
  if Value then Result := '1' else Result := '0';
end;

function InitializeSetup: Boolean;
var Parts: TVersionParts; Report, Content: String;
begin
  Result := True;
  if not SwitchPresent('VERIFYONLY') then Exit;
  { Safe packaging verification: no runtime execution, app install, shortcuts or registry writes. }
  Result := False;
  Report := ExpandConstant('{param:VERIFYREPORT|}');
  if Report = '' then Exit;
  try
    if not CompatibleDotNetVersion('{#DotNetVersion}') or CompatibleDotNetVersion('11.0.0') or
      CompatibleDotNetVersion('10.0.0') or CompatibleDotNetVersion('10.0.14-preview') or
      not CompatibleDotNetVersion('10.0.999') or ParseVersion('1..3', Parts) or
      ParseVersion('1.2.3.4.5', Parts) or not VersionAtLeast('156.0.0.0', '{#WebViewVersion}') then
      RaiseException('Version boundary validation failed.');
    CheckPayload('{#DotNetFile}', '{#DotNetSha256}');
    CheckPayload('{#WebViewFile}', '{#WebViewSha256}');
    Content := 'Verified=1' + #13#10 + 'VersionRules=1' + #13#10 +
      'DotNetRequired=' + Flag(not DotNetPresent) + #13#10 +
      'WebView2Required=' + Flag(not WebViewPresent) + #13#10 +
      'DotNetMinimum={#DotNetVersion}' + #13#10 + 'WebView2Minimum={#WebViewVersion}' + #13#10;
    if not SaveStringToFile(Report, Content, False) then RaiseException('Could not write verification report.');
  except
    SaveStringToFile(Report, 'Verified=0' + #13#10 + GetExceptionMessage, False);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var ExitCode: Integer;
begin
  Result := '';
  try
    if not DotNetPresent then begin
      WizardForm.StatusLabel.Caption := 'Installing .NET Desktop Runtime {#DotNetVersion} (Windows approval may be required)...';
      CheckPayload('{#DotNetFile}', '{#DotNetSha256}');
      if not ShellExec('runas', ExpandConstant('{tmp}\{#DotNetFile}'),
        '/install /quiet /norestart', '', SW_SHOWNORMAL, ewWaitUntilTerminated, ExitCode) then begin
        Result := '.NET installation approval was cancelled or could not start. ' + SysErrorMessage(ExitCode); Exit;
      end;
      if ExitCode = 3010 then begin
        NeedsRestart := True; Result := 'Restart Windows, then run this installer again to finish installing Windows Alarm.'; Exit;
      end;
      if (ExitCode <> 0) or not DotNetPresent then begin Result := '.NET Desktop Runtime installation failed. Code: ' + IntToStr(ExitCode); Exit; end;
    end;
    if not WebViewPresent then begin
      WizardForm.StatusLabel.Caption := 'Installing Microsoft Edge WebView2 Runtime...';
      CheckPayload('{#WebViewFile}', '{#WebViewSha256}');
      if not Exec(ExpandConstant('{tmp}\{#WebViewFile}'), '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then begin
        Result := 'WebView2 installer could not start. ' + SysErrorMessage(ExitCode); Exit;
      end;
      if (ExitCode <> 0) or not WebViewPresent then begin Result := 'WebView2 installation failed. Code: ' + IntToStr(ExitCode); Exit; end;
    end;
  except Result := GetExceptionMessage; end;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo,
  MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo + NewLine + NewLine + '.NET Desktop Runtime {#DotNetVersion}: ';
  if DotNetPresent then Result := Result + 'compatible version already installed' else
    Result := Result + 'install bundled package (administrator approval required)';
  Result := Result + NewLine + 'WebView2 Runtime {#WebViewVersion} or newer: ';
  if WebViewPresent then Result := Result + 'already installed' else Result := Result + 'install bundled offline package';
  Result := Result + NewLine + NewLine + MemoTasksInfo;
end;
