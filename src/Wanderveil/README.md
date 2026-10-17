# Wanderveil source map

`Wanderveil.csproj` is built by root `Wanderveil.slnx`. `Plugin.cs` owns host composition/lifecycle and `Configuration.cs` stores reduced motion. The shell, settings, shared UI foundations, synthetic Passport identity view, and static Discover cards/details exist; other areas remain reserved.

| Directory | Responsibility |
| --- | --- |
| `Application/` | Use cases and view state |
| `Domain/` | Host-independent passport, stamp, discovery models/interfaces |
| `Features/Discover/` | Synthetic responsive cards/details and in-memory navigation; collection later |
| `Features/Passport/` | Synthetic responsive identity/notes view; collection/persistence later |
| `Features/Echoes/` | Future adventure presentation |
| `Features/Studio/` | Creator templates/editor/preview |
| `Features/Social/` | Future consent/request UI |
| `Features/Settings/` | Implemented appearance window; future privacy/account UI |
| `UI/` | Shell, primitives, typography, page/button/card/stamp-preview components, and procedural compass |
| `Infrastructure/` | Dalamud, local persistence, future API/media/rendering adapters |

Keep one plugin project and only classes needed by the current slice. Domain/application behavior must not depend on drawing APIs, private backend projects, or optional graphics. The sample Passport is presentation-only; no collection, identity authentication, storage, creator tools, or online services are implemented.
