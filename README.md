# Nori Language

A clear, friendly programming language for VRChat worlds that compiles to Udon Assembly.

## Installation

**Recommended:** Install via [VRChat Creator Companion](https://vcc.nori-lang.dev). Click **Add to VCC** on that page, then add the Nori package to your project from the VCC package list.

Alternatively, add the package directly in Unity via **Window > Package Manager > Add package from git URL** using `https://github.com/norilang/nori.git?path=Packages/dev.nori.compiler`.

## Usage

Create `.nori` files in your Assets folder. They compile automatically on save.

```nori
pub let speed: float = 5.0

on Start {
    log("Hello from Nori!")
}

on Interact {
    log("You clicked me!")
}
```

## Editor Support

Nori includes an LSP server that provides real-time editor features for `.nori` files:

- Diagnostics (error squiggles as you type)
- Autocomplete (types, members, events)
- Hover information (types, signatures, extern mappings)
- Go-to-definition
- Signature help
- Document outline

Supported editors: **VS Code** (dedicated extension), **JetBrains Rider**, and **Visual Studio**.

**Quick start:** In Unity, go to **Tools > Nori > Setup Editor...** to launch the setup wizard. It builds the LSP server and configures your editor in a few clicks.

See [nori-lang.dev/editors](https://nori-lang.dev/editors/) for full setup instructions.

## Documentation

See [nori-lang.dev](https://nori-lang.dev) for full documentation.
