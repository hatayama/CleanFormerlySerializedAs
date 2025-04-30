# Clean Formerly Serialized As

Clean Formerly Serialized As is an editor extension for Unity to remove unnecessary `[FormerlySerializedAs("oldName")]` attributes from your project.

## How to Use
Right click on a C# file or directory → select “Clean FormerlySerializedAs”.
![](https://raw.githubusercontent.com/hatayama/CleanFormerlySerializedAs/refs/heads/main/Assets/Images/1.png)

## Features

- Detects and removes unnecessary `FormerlySerializedAs` attributes remaining after refactoring.
- Scans project assets (scenes, prefabs, ScriptableObjects, etc.).
- Helps maintain a clean and maintainable project.

## Installation

### Via Unity Package Manager

1. Open Window > Package Manager.
2. Click the "+" button.
3. Select "Add package from git URL".
4. Enter the following URL:
   ```
   https://github.com/your-org/CleanFormerlySerializedAs.git
   ```

### Via OpenUPM

### How to use UPM with Scoped registry in Unity Package Manager
1. Open the Project Settings window and navigate to the Package Manager page.
2. Add the following entry to the Scoped Registries list:
```
Name：OpenUPM
URL: https://package.openupm.com
Scope(s)：io.github.hatayama
```
![](https://github.com/hatayama/_Docs/blob/main/Images/upm_pckage1.png?raw=true)

3. Open the Package Manager window, navigate to the "hatayama" page in the My Registries section.
![](https://github.com/hatayama/_Docs/blob/main/Images/upm_pckage2.png?raw=true)


### Command
```bash
openupm add io.github.hatayama.cleanformerlyserializedas
```


## License

MIT License

## Author

Masamichi Hatayama

## License

MIT License (tentative; change if necessary)

## Author

Your Name (tentative; change if necessary) 