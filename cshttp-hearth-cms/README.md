# cshttp Hearth CMS

Hearth CMS running as a standalone C# console application: **no ASP.NET, no System.Web, no IIS pipeline, no ASP.NET Core, no Kestrel.**
This is the proof of concept for the *Zero-Framework C#* architecture (TcpListener + embedded cshttp + Pageless handlers behind a reverse proxy).

```
Browser ──HTTPS──► nginx (Linux) / IIS + HttpPlatformHandler (Windows)
                     │ static: /css /js /assets /fonts /media served from disk (nginx)
                     ▼ HTTP/1.1 over 127.0.0.1
               HearthCms (console exe, supervised by systemd / IIS)
                 Host/WebServer.cs   TcpListener accept loop, keep-alive, limits, graceful drain
                 cshttp/*.cs         RFC 9112 parser (embedded source, 19 files)
                 Web/*.cs            System.Web-shaped facade (HttpContext, Request, Response, cookies, files, MapPath)
                 Global.cs           the original Global.asax router, ported line for line
                 engine/**           the original Hearth CMS code, essentially unchanged
```

## Project layout

| Path | What |
|---|---|
| `src/HearthCms/Program.cs` | entry point, config (args / env), Application_Start, SIGTERM handling |
| `src/HearthCms/Host/` | TcpListener server, static file server (ETag/304/Range), logging |
| `src/HearthCms/Web/` | the facade that lets the Pageless handlers run unmodified |
| `src/HearthCms/cshttp/` | cshttp source, compiled in (no DLL/NuGet) |
| `src/HearthCms/engine/` | Hearth CMS (handlers, templates, Markdown, SQLite, themes) |
| `src/HearthCms/{css,js,fonts,assets,media,App_Data}` | content root (App_Data is never served) |
| `deploy/windows/` | `web.config` (HttpPlatformHandler) + `install-iis.ps1` |
| `deploy/linux/` | `nginx-hearthcms.conf`, `hearthcms.service`, `install.sh` |
| `tools/publish.ps1` | self-contained builds for win-x64 and linux-x64 |
| `tools/smoke-test.ps1` | 41-check end-to-end test against any running instance |

## What changed from the IIS version

* `System.Web` → `Web/` facade over cshttp (each engine file just gained `using CsHttp.Web;`).
* `Global.asax.cs` → `Global.cs`; static files are served by the app/nginx, and only the public folders are servable.
* `System.Drawing` (Windows-only) → SkiaSharp in `ImageThumb` and `FaviconGenerator`.
* `BuildManager.GetReferencedAssemblies()` → loaded assemblies (C# theme discovery).
* `MapPath` resolves path case on Linux (the CMS was written on a case-insensitive file system).
* `Analytics.FlushOnShutdown()` writes queued visits on graceful stop.
* `App_Data/config.txt` ships with `dev_mode=false`. **`dev_mode=true` auto-logs every visitor into admin, so never deploy with it.**

Packages: `System.Data.SQLite.Core` and `Newtonsoft.Json` (both used by the original), plus `SkiaSharp`. Target: .NET 10, published self-contained, so the servers need no runtime installed.

## Run locally

```bash
dotnet run --project src/HearthCms -- --port 5080
```

Then open http://127.0.0.1:5080. Options: `--port`, `--bind`, `--contentroot`, `--quiet`. You can also set them with the environment variables `HEARTH_PORT`, `HEARTH_BIND` and `HEARTH_CONTENT_ROOT`, and IIS passes `HTTP_PLATFORM_PORT`.

## Deploy

```bash
powershell -ExecutionPolicy Bypass -File tools/publish.ps1
```

**Windows / IIS** needs IIS with the HttpPlatformHandler module. No ASP.NET feature or hosting bundle is required. Run from an elevated shell:

```bash
powershell -ExecutionPolicy Bypass -File deploy/windows/install-iis.ps1 -Port 8092
```

**Linux / nginx** (Debian/Ubuntu). Copy `publish/hearthcms-linux-x64.tar.gz` and the files in `deploy/linux/` to the server, then run:

```bash
sudo ./install.sh hearthcms-linux-x64.tar.gz
```

Re-running either installer upgrades the binaries and keeps `App_Data` and `media`.

## Notes

* Sessions live in process memory, the same as the original app. Keep one process per site (IIS `processesPerApplication="1"`). "Remember me" logins survive restarts.
* Logs: stdout goes to journald or the IIS stdout log, and errors also go to `App_Data/logs/error-YYYYMMDD.log`.
* To reset admin access, put `App_Data/reset_admin.txt` in place (see the `.example` file) and visit `/reset_app`.
