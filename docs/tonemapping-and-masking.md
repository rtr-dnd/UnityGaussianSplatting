# Tonemapping & Masking

This package allows for splat-specific color correction and 3D volume-based masking (clipping) of Gaussian Splatting results, independent of the overall scene post-processing.

## 1. Color Adjustments

You can adjust the color and brightness of the splats from the Inspector of the `GaussianSplatRenderer` component.

*   **Color Tint**: Applies a color tint to the entire splat object. This is useful for matching the splat's lighting with the scene or controlling its exposure independently.
*   **Tone Curve**: Uses Unity's `AnimationCurve` editor to customize the brightness response curve.
    *   **Enhancing Contrast**: Create an S-curve to add punch and definition to the splat.
    *   **Suppressing Highlights**: Lower the top-right value to prevent overexposure in bright areas.

## 2. Alpha Masking (Recommended)

Use 3D object shapes to "softly" fade out splats or reveal them only in specific areas. Since this is calculated per-pixel in screen space, the result is extremely smooth.

### Setup Instructions
1.  **Prepare a Layer**: Go to `Edit > Project Settings > Tags and Layers` and create a new layer (e.g., `GaussianMask`).
2.  **Configure Renderer Feature**: In your URP Renderer Asset, find the `GaussianSplatURPFeature` and assign your newly created layer to the `Mask Layer` property.
3.  **Place Mask Objects**:
    *   Place objects like `Plane`, `Cube`, or `Sprite` in your scene.
    *   Set the **Layer** of these objects to `GaussianMask`.
    *   Create a new material, set its shader to **`Gaussian Splatting/Alpha Mask`**, and assign it to the objects.

### Behavior
*   By default, **splats are hidden where there is no mask object**.
*   Splats are revealed where a mask object is present, based on that object's transparency (Alpha value).
*   **Using Textures**: By assigning a gradient texture to the mask material, you can clip splats with complex shapes or soft, smoky boundaries.

## 3. Stencil Masking

Uses the stencil buffer to perform hard, binary clipping.

### Setup Instructions
1.  **Mask-side Setup**:
    *   Apply a material using the **`Gaussian Splatting/Stencil Mask`** shader to an object.
    *   Set `Stencil Ref` to a value (e.g., 1).
2.  **Renderer-side Setup**:
    *   In `GaussianSplatRenderer`, set `Stencil Ref` to the same value (e.g., 1).
    *   Set `Stencil Comp` to `Equal` to show only inside the mask, or `NotEqual` to cut out the masked area.

## Comparison of Masking Features

| Feature | Boundary Appearance | Key Characteristics | Recommended Use |
| :--- | :--- | :--- | :--- |
| **Alpha Mask** | Smooth / Blurred | Control opacity via textures or Alpha values | Artistic blending, fading, scene integration |
| **Stencil Mask** | Hard (Aliased) | Uses stencil buffer. Extremely lightweight | Simple cutouts, debugging |
| **Cutout (Existing)** | Granular | Based on splat "center points". Splats disappear as whole units | Integration with editing tools, deleting specific objects |

---
**Note**: These features are designed to work on the URP (Unity 6 or later) Render Graph system.