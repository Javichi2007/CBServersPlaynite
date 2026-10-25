# CB Servers para Playnite

<p align="center">
  <img src="Assets/cb-icon.png" alt="CB Servers" width="128">
</p>

<p align="center">
  Extensión no oficial de <a href="https://github.com/CBServers/cb-launcher">CB Servers Launcher</a> para Playnite.
</p>

<p align="center">
  <a href="https://github.com/Javichi2007/CBServersPlaynite/releases/latest">Descargar</a> ·
  <a href="https://github.com/Javichi2007/CBServersPlaynite/releases">Releases</a> ·
  <a href="https://github.com/Javichi2007/CBServersPlaynite/issues">Issues</a>
</p>

Añade una entrada **CB Servers** en la barra lateral de Playnite con el catálogo de Call of Duty de CB, jugadores y servidores en vivo, y botones para instalar o jugar usando tu launcher ya instalado.

## Características

- **Entrada propia en la barra lateral** con el logo de CB.
- **18 juegos** del catálogo de CB: CoD 1, UO, 2, 4, WaW, BO1, MW2, BO2, MW3, BO3, Ghosts, AW, MWR, IW, WWII, MW2R, BO4 y HorizonMW.
- **Jugadores y servidores en vivo** desde la API pública de CB (`/v1/player-counts`).
- **Navegador de servidores** por juego (`/v1/servers`) con mapa, modo, región, jugadores, bots y servidores destacados.
- **Detección automática de `cb-launcher.exe`**: protocolo `cbservers://`, carpeta de descargas, escritorio, `%LOCALAPPDATA%\cbservers` y la carpeta del plugin.
- **Instalación y lanzamiento** con `-install`, `-launch` y `-mode`, o mediante el protocolo `cbservers://` si el launcher ya está abierto.
- **Estado real de instalación** leído de `%LOCALAPPDATA%\cbservers\user\properties.json`.
- **Ajustes** de ruta del launcher, juego del navegador de servidores y opciones de CB (cerrar al lanzar, modo offline).
- Interfaz en español, sin telemetría ni servicios propios.

## Requisitos

- Windows 10/11.
- [Playnite](https://playnite.link/) 10 o superior (probado con 10.62).
- [CB Servers Launcher](https://github.com/CBServers/cb-launcher) instalado. El plugin puede detectarlo solo, pero también puedes indicar la ruta manualmente.

## Instalación

1. Descarga el último `CBServersPlaynite_x.y.z.pext` desde [Releases](https://github.com/Javichi2007/CBServersPlaynite/releases/latest).
2. En Playnite ve a **Add-ons → Install add-on from file** y selecciona el `.pext` (o haz doble clic sobre el archivo).
3. Reinicia Playnite si la entrada **CB Servers** no aparece al momento.

La extensión no incluye el launcher ni los juegos: solo los abre y consulta sus datos.

## Uso

- Entra en **CB Servers** desde la barra lateral.
- Pulsa **INSTALAR** si el juego no está instalado o **JUGAR** si ya lo está. Si el juego tiene varios modos, aparecerá un menú para elegir campaña, multijugador, zombis, etc.
- En la pestaña **Servidores** elige un juego para ver sus servidores en vivo.
- Si el launcher no se detecta, abre **Ajustes**, pulsa **Examinar** y selecciona `cb-launcher.exe`.
- El botón de la carpeta de datos abre `%LOCALAPPDATA%\cbservers` para revisar la instalación.

Juegos sin navegador de servidores en la API de CB (BO3, Infinite Warfare, MW2R y HorizonMW) muestran 0 servidores; el resto de funciones sigue disponible.

## Cómo funciona

- La ruta del launcher se guarda en `ExtensionsData/<plugin-id>/settings.json` de Playnite.
- Los datos de instalación y el nombre de jugador se leen del archivo local de CB, sin enviarlos a ningún sitio.
- Las consultas de jugadores y servidores van a `https://servers.cbservers.xyz` (con `https://servers.cbservers.dev` como alternativa).
- Los juegos se lanzan con el protocolo `cbservers://` cuando el launcher ya está en ejecución, o con los argumentos `-launch <juego> [-mode <modo>]`.

## Compilar

Necesitas el [SDK de .NET 8](https://dotnet.microsoft.com/download) y Windows (WPF, `net462`).

```powershell
git clone https://github.com/Javichi2007/CBServersPlaynite.git
cd CBServersPlaynite
dotnet build -c Release
```

Para generar el `.pext` en `dist/`:

```powershell
.\build.ps1
```

El script lee la versión de `extension.yaml`, compila, copia el DLL y los assets, y crea `dist/CBServersPlaynite_<version>.pext`. Usa `-SkipBuild` si ya compilaste antes.

### Estructura

```
CBServersPlaynite/
├─ src/
│  ├─ CBServersPlugin.cs      # Entrada del plugin y entrada del sidebar
│  ├─ CBServersView.xaml      # Interfaz
│  ├─ CBServersView.xaml.cs   # Lógica de la vista
│  ├─ LauncherService.cs      # Detección del launcher, API de CB y lanzamiento
│  ├─ Models.cs               # Catálogo de juegos y modelos
│  ├─ Settings.cs             # Ajustes persistidos
│  └─ Converters.cs           # Convertidores WPF
├─ Assets/                    # Iconos y portadas de los juegos
├─ extension.yaml             # Manifiesto de Playnite
├─ build.ps1                  # Empaquetado .pext
└─ .github/workflows/build.yml
```

## Aviso legal y licencia

Proyecto **no oficial**, hecho por la comunidad. No está afiliado ni respaldado por CBServers ni por Activision. Call of Duty y sus portadas pertenecen a sus respectivos propietarios.

El código se distribuye bajo **GPL-3.0** (ver [LICENSE](LICENSE)), en parte porque las portadas y el logo provienen de [CBServers/cb-launcher](https://github.com/CBServers/cb-launcher), también publicado bajo GPL-3.0. CB Servers Launcher no se incluye en este repositorio.

Créditos a [CBServers](https://github.com/CBServers) por el launcher y su API.

---

## English

Unofficial Playnite extension for [CB Servers Launcher](https://github.com/CBServers/cb-launcher). It adds a **CB Servers** sidebar entry with the Call of Duty catalog, live player/server counts, a per-game server browser, and launch/install buttons that reuse your existing launcher.

**Install:** download the latest `.pext` from Releases and use *Add-ons → Install add-on from file*, then restart Playnite. The extension can auto-detect `cb-launcher.exe`; otherwise set the path in its settings.

**Requirements:** Windows, Playnite 10+, and CB Servers Launcher. The launcher and the games are not bundled. Network calls go only to CB's public server API.

**Build:** `dotnet build -c Release`, then `.\build.ps1` to produce `dist/CBServersPlaynite_<version>.pext` (requires .NET SDK 8).

Licensed under **GPL-3.0**. Not affiliated with CBServers or Activision. Game artwork is taken from CBServers/cb-launcher (GPL-3.0).
