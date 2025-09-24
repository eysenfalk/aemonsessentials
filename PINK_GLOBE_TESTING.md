# Testing the Pink Globe Debug Renderer

## 🔧 Setup
1. **F5** in VS Code to launch Vintage Story with your mod
2. Create/load a world
3. Get some creatures nearby (spawn wolves, drifters, etc.)

## 🌸 Pink Globe Commands

### Enable Debug Visualization
```
.stealthdebug on
```
This should show:
- **Pink translucent spheres** around each entity (detection range)
- **Yellow view cones** showing where entities are looking  
- **Green/Red lines** showing line-of-sight to you
- **Purple circle** above your head when sneaking

### Disable Debug Visualization
```
.stealthdebug off
```

## 🎯 What You Should See

**Normal Mode (not sneaking):**
- Pink spheres around entities (15 block detection range)
- Green lines if entity can see you, red if blocked
- Yellow view cones showing entity facing direction

**Sneaking Mode:**
- Smaller pink spheres (reduced detection range due to stealth multiplier)
- Purple circle above your head indicating stealth mode
- Same line-of-sight visualization

## 🐛 Troubleshooting

**If nothing appears:**
1. Make sure you're close to entities (within ~30 blocks)
2. Try `.stealthinfo` to verify entities are detected
3. Check that you have EntityAgent creatures (not passive animals)
4. Try spawning a wolf: `/entity spawn wolf`

**Performance Issues:**
- The renderer updates 60 times per second when active
- Turn off when not needed: `.stealthdebug off`

## 🧪 Testing Scenarios

1. **Approach Detection Range:**
   - Walk toward entity normally
   - Watch line turn green when in detection range
   - Sneak and watch sphere shrink

2. **Line of Sight Testing:**
   - Hide behind walls/trees
   - Watch line turn red when blocked
   - Move around corners and watch it change

3. **View Cone Testing:**
   - Circle around entities
   - See if detection works differently from behind vs front
   - Yellow cones show their facing direction

The pink spheres should be clearly visible and show the exact detection boundaries for testing your stealth system!