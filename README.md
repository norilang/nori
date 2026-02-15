# Nori Language

A clear, friendly programming language for VRChat worlds that compiles to Udon Assembly.

## Installation

Add via Unity Package Manager using the git URL, or copy the package to your project's `Packages/` directory.

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

## Documentation

See [nori-lang.dev](https://nori-lang.dev) for full documentation.
