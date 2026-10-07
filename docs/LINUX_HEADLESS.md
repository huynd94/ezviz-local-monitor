# Ubuntu 24.04 x64 headless

**Stable release: v1.9.0**, supporting Ubuntu 24.04 x64 headless. The user confirmed successful testing and application operation on Ubuntu 24.04 and approved the stable release. Windows desktop remains available. Detailed automated/operator evidence is recorded in `docs/qa/linux-headless-acceptance.md`.

## Package and install

Download the Linux `.tar.gz` and matching `.tar.gz.sha256sum` from the same trusted release. The Linux checksum suffix differs from Windows `.zip.sha256` so older Windows updaters cannot select it accidentally.

[Download v1.9.0](https://github.com/huynd94/ezviz-local-monitor/releases/tag/v1.9.0): `EZVIZ-Local-Monitor-Linux-Headless-x64-v1.9.0.tar.gz` and its `.sha256sum`.

```bash
python3 scripts/linux/Verify-Package.py EZVIZ-Local-Monitor-Linux-Headless-x64-v1.9.0.tar.gz EZVIZ-Local-Monitor-Linux-Headless-x64-v1.9.0.tar.gz.sha256sum --extract /tmp/ezviz-package
sudo bash /tmp/ezviz-package/installer/linux/install.sh --package-root /tmp/ezviz-package
```

The verifier is shipped in the source repository. Verify the checksum and reject unsafe archive entries before privileged installation. Install scripts accept an already verified/extracted directory, not an arbitrary archive or download URL.

Requirements: Ubuntu24.04 x64, systemd, Python3 for verification, standard GNU utilities, readelf, ldd, runuser and flock. The product is self-contained .NET8 but requires standard native system libraries. Headless OpenCV has no GTK/X11/Wayland dependency. Optional .NET LTTng tracing provider is excluded because its ABI0 dependency is unavailable on Ubuntu24.04; application file/journald logging and EventPipe remain available.

Installation layout:

```text
/opt/ezviz-local-monitor/releases/VERSION/app/ezviz-headless
/opt/ezviz-local-monitor/current -> releases/VERSION
/var/lib/ezviz-local-monitor/       # service-owned0700 state
/etc/systemd/system/ezviz-local-monitor.service
```

New install creates nologin `ezviz-monitor`, but does not enable/start the service or create camera configuration.

## Configure over SSH

```bash
APP=/opt/ezviz-local-monitor/current/app/ezviz-headless
sudo -u ezviz-monitor "$APP" configure
sudo -u ezviz-monitor "$APP" config validate
sudo -u ezviz-monitor "$APP" doctor
sudo systemctl enable --now ezviz-local-monitor
sudo -u ezviz-monitor "$APP" status --json
sudo journalctl -u ezviz-local-monitor -f
```

The wizard hides existing text/secrets and reads URL/token/key fields without echo. To automate, pipe PascalCase AppSettings JSON into `configure --stdin`; do not pass secrets in arguments/history. Stop the service before configure/import: mutations share the daemon lock. Empty camera configuration is valid idle state.

```bash
printf '{"Cameras":[]}' | sudo -u ezviz-monitor "$APP" configure --stdin
sudo -u ezviz-monitor "$APP" backup import /secure/config.ezviztransfer
sudo -u ezviz-monitor "$APP" backup export /secure/config.ezviztransfer
sudo -u ezviz-monitor "$APP" alerts test --channel telegram
```

Backup prompts do not echo passwords. For explicit stdin automation, import reads one password line, export reads password plus confirmation on two lines. Use a secure pipe/secret manager, never a password argument. DPAPI `.ezvizbackup` remains Windows-only; `.ezviztransfer` works across hosts.

Linux master key is32 bytes, separate from encrypted settings, both service-owned0600. State/keys directories are0700. Do not use a Windows-mounted `/mnt/c` or `/mnt/d` state directory. Losing the master key prevents local settings decryption; restore the original key or import a portable backup into new state. Keep complete state/key backups secured separately from distribution packages.

## Operation

- Daemon runs foreground without DISPLAY, GUI/tray or HTTP listener.
- systemd controls restart; no extra Windows watchdog process.
- Scheduler uses host timezone (`TZ` override is possible in service configuration); doctor/status display the effective timezone. Check it when moving Windows schedules to a server.
- Heartbeat5 seconds, stale30 seconds; PID + kernel start identity prevents a reused PID being treated as the old daemon.
- Retention runs every24 hours using RetentionDays, excluding pending events, leased/shared images and unsafe paths.
- SIGTERM/SIGINT triggers bounded shutdown; work that cannot finish is reported as failure, not healthy termination.
- Exit codes:0 success,1 operational/native,2 config/arguments,3 lock conflict,4 stale/unavailable status. systemd does not restart on2/3.

## Upgrade and rollback

Verify/extract the new archive, then:

```bash
sudo bash /tmp/ezviz-new/installer/linux/update.sh --package-root /tmp/ezviz-new
```

Update stages a new immutable release, preserves state/key, keeps enabled/active state and performs config/native health checks. Failure rolls back `current` and service state; previous releases are retained. Reinstalling the same version with different content is refused. Use a new version rather than changing an installed version directory.

For an explicit rollback, run the previously verified old package's `update.sh --package-root OLD_PACKAGE`. State compatibility must be checked if a future release introduces schema changes.

```bash
sudo bash /opt/ezviz-local-monitor/current/installer/linux/uninstall.sh
```

Uninstall removes the managed unit/program, but keeps service account and state/key by default. Back up state before an explicit, separately approved data removal.

## Build and test

```bash
bash scripts/linux/Run-NativeProbe.sh headless5 true
dotnet test tests/EzvizLocalMonitor.Tests -c Release
bash scripts/linux/Test-Headless.sh
python3 tests/linux/Package-Security.py
bash scripts/linux/Package-Headless.sh 1.9.0
bash tests/linux/Package-Smoke.sh artifacts/dist/EZVIZ-Local-Monitor-Linux-Headless-x64-v1.9.0.tar.gz
```

Root/live service tests are opt-in and require approval on a dedicated test host. CI publishes build artifacts only, not GitHub releases. Automated WSL acceptance and the user's Ubuntu 24.04 success report are recorded separately; a specific 24-hour soak or reboot result is not inferred from the user's general confirmation.
