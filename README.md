## 📦 Scene Testing Guide

The current test scenes are located at: `Assets/Scenes/KitchenWithPeople`

Available scenes:
- **KitchenWithPeople**
- **KitchenWithMovingPeople**
- **KitchenWithDancingPeople**

Each scene represents different types of human behaviors for testing.

## 🎥 Camera Configuration

In each scene, the camera setup can be found at: `Main Camera → CameraController.cs`

### 🔁 Reset Behavior

The `CameraController` allows you to configure the camera's randomized position after reset.

You can adjust:

- The **Randomize Position Settings**
- The **Randomize Rotation Settings**

This is useful for testing from different viewpoints without manually repositioning the camera.

---

## 🧪 Notes

- Make sure to review the `CameraController` settings before running the scene.
- Different scenes are designed for testing different motion patterns and interactions.
