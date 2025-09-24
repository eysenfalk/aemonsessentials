# AemonsEssentials Debug Guide

## VS Code Debugging Setup ✓

You can now run and debug your mod directly in VS Code:

1. **F5** or **Ctrl+Shift+D** → Select "Launch VS Client" or "Launch VS Server"
2. This will start Vintage Story with your mod loaded
3. Set breakpoints in your C# code and they'll work just like in Rider!

## Visual Debugging Tools

### StealthDebugRenderer (3D Visualization)
- Use `.stealthdebug on` in-game to enable 3D line rendering
- Shows detection ranges as circles around entities
- Shows view cones and line-of-sight rays
- Use `.stealthdebug off` to disable

### StealthInfo (Text Output)
- Use `.stealthinfo` in-game for detailed stealth information
- Shows your position, sneaking status, nearby entities
- For each entity: distance, detection status, line-of-sight
- Displays in chat window (easier to read than 3D overlay)

## Testing Your Stealth System

1. **Start the game** with F5 in VS Code
2. **Create a world** and spawn some creatures (wolves, drifters, etc.)
3. **Use `.stealthinfo`** to see current stealth status
4. **Try different scenarios:**
   - Normal walking vs sneaking
   - Behind walls/obstacles
   - Different distances from entities
   - Moving around corners

### Key Information Displayed:
- **Position**: Your current coordinates  
- **Sneaking**: Whether you're currently sneaking
- **Detection Multiplier**: How much sneaking reduces detection range
- **Per Entity**:
  - Distance to entity
  - Detection status (HIDDEN, DETECTED, NO LINE OF SIGHT)
  - Normal vs stealth detection ranges
  - Line-of-sight calculation result

## Debug Commands Summary

| Command | Description |
|---------|-------------|
| `.stealthinfo` | Show detailed stealth info in chat |
| `.stealthdebug on` | Enable 3D visual debugging |
| `.stealthdebug off` | Disable 3D visual debugging |

## Troubleshooting

- **No entities showing?** Move closer (20 block scan range)
- **Detection seems wrong?** Check if entity is EntityAgent type
- **3D renderer not working?** Use `.stealthinfo` instead (more reliable)
- **Breakpoints not hitting?** Make sure you're using F5 launch, not external game

The text-based `.stealthinfo` command is the most reliable way to debug your stealth mechanics!