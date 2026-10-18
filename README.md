# Zero Framework C# Web Application

Github Repository: https://github.com/adriancs2/zero-framework-csharp-web-app

In the C# ecosystem, building a web application has historically meant choosing between two main frameworks:

- **ASP.NET (System.Web)**, tightly coupled with Windows and the IIS managed runtime pipeline.
- **ASP.NET Core**, running on Kestrel with a heavy middleware stack and extensive NuGet dependencies.

Both frameworks provide a vast array of built-in features. But above all, their foundational component is the **HTTP Wire Parser**—a critical piece that historically did not exist as a standalone, dependency-free C# library... **until `cshttp`**.

This repository is a production-grade **Proof of Concept**. 

By embedding `cshttp` (RFC 9112 HTTP parser in pure C#), developers can now engineer their own web architectures entirely free from framework lock-in. This is a pure C# web application that runs without:
- No ASP.NET / `System.Web`
- No IIS managed pipeline
- No ASP.NET Core / Kestrel
- No external hosting builders or pre-built middleware

You engineer everything from the ground up: a raw `TcpListener` accepting sockets asynchronously, passing clean request objects to synchronous, high-performance handlers.

It unlocks a world of architectural possibilities:
- Build hyper-specialized, zero-dependency microservices.
- Design custom routing, session, and lifecycle models tailored exactly to your workload.
- Explore alternative architectures—whether simpler, different, or better suited to your needs.

### What is Inside This Repository?

This project combines two distinct components into a working showcase:
1. **`cshttp`**: The embedded, specification-compliant HTTP/1.1 wire parser.
2. **`Hearth CMS`**: A real-world, database-driven CMS ported from a "Pageless ASP.NET Web Forms" design to run on raw sockets behind Nginx (Linux) or IIS with HttpPlatformHandler (Windows).

The architecture demonstrated here is just **ONE** way to engineer a zero-framework C# web server. The possibilities are limited only by your imagination.

---

To dive into the engineering details, benchmarks, and deployment blueprints of this demo, check out [zero-framework-csharp-web-app/README.md](zero-framework-csharp-web-app/README.md).

Hearth CMS:
- https://github.com/ASP-NET-Web-Forms-Club/Hearth-CMS

cshttp:
- https://github.com/adriancs2/cshttp