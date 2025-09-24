- # Aemon's Essentials - Multi-Module Mod Specification

## Overview
A comprehensive Vintage Story mod containing three essential improvements to enhance gameplay experience. Each module is independent but shares common configuration and infrastructure.

## Module Structure

### 📚 Module 1: Smart Handbook
**Purpose**: Remember the last opened handbook page for better user experience
**Priority**: Phase 1 (Simple UI enhancement)

#### Features
- Save last opened handbook page when manually opened (not via item hover)
- Restore saved page on next manual handbook opening
- Distinguish between hover-triggered vs manual handbook opens
- Configurable enable/disable option

#### Implementation Plan
```
HandbookMemory/
├── HandbookMemorySystem.cs     // Main mod system with Harmony patches
├── HandbookMemoryConfig.cs     // Configuration (enable/disable, page limit)
├── HandbookState.cs           // Tracks handbook state and last page
└── HandbookPatches.cs         // Harmony patches for handbook GUI
```

#### Technical Approach
- Patch handbook GUI opening methods to detect manual vs hover triggers
- Store last page ID in mod configuration file
- Intercept handbook initialization to restore saved page
- Use lightweight state tracking to minimize performance impact

---

### 🥷 Module 2: Advanced Stealth System
**Purpose**: Realistic stealth mechanics with sneaking detection reduction and line-of-sight
**Priority**: Phase 2 (Moderate complexity - game mechanics)

#### Features
- **Sneak Detection**: Reduce entity detection range by 50% (configurable) when sneaking
- **Line of Sight**: Entities can't detect players through solid blocks
- **AI Memory**: Entities remember last known player position for 5 seconds (configurable)
- **Smart Tracking**: AI continues pursuing to last known location when vision is blocked
- **Performance Optimized**: Memory cleanup and efficient raycasting

#### Implementation Plan
```
StealthSystem/
├── StealthModSystem.cs         // Main coordination and Harmony setup
├── StealthConfig.cs           // All stealth configuration options
├── StealthUtils.cs            // Line-of-sight, distance calculations
├── AIMemoryManager.cs         // Entity memory tracking system
├── StealthPatches.cs          // Harmony patches for entity detection
└── PlayerMemory.cs            // Data structure for AI memory
```

#### AI Behavior Logic
1. **Normal Detection**: Standard vanilla range and behavior
2. **Sneak Detection**: Multiply detection range by `sneakMultiplier` (default 0.5)
3. **Line of Sight Check**: Raycast between entity eyes and player position
4. **Memory System**: When vision blocked, AI remembers last position for `memoryDuration`
5. **Tracking Behavior**: AI moves toward remembered position, scanning for player
6. **Memory Decay**: Forgotten after timeout, AI returns to normal behavior

#### Performance Considerations
- Memory cleanup every 5 seconds to prevent leaks
- Efficient raycasting with 0.5 block steps
- Batch processing of nearby entities
- Configurable systems to disable expensive features

---

### ⚙️ Module 3: Unified Configuration System
**Purpose**: Advanced configuration management with ConfigLib integration
**Priority**: Phase 3 (Infrastructure enhancement)

#### Features
- **ConfigLib Integration**: Modern GUI-based configuration interface
- **Live Reload**: Change settings without restarting game
- **Profile Management**: Multiple configuration profiles
- **Import/Export**: Share configurations between worlds/players
- **Validation**: Ensure configuration values are within safe ranges

#### Implementation Plan
```
ConfigurationSystem/
├── ModConfigManager.cs        // Main configuration coordinator
├── ConfigLibIntegration.cs    // ConfigLib GUI integration
├── ConfigValidator.cs         // Value validation and sanitization
├── ConfigProfiles.cs          // Profile management system
└── ConfigPatches.cs           // Live reload support via Harmony
```

---

## Development Phases

### Phase 1: Foundation & Smart Handbook
**Goal**: Establish mod structure and implement handbook memory
**Complexity**: ⭐⭐ (Beginner)
**Duration**: 1-2 days

**Tasks:**
1. Create base mod project structure
2. Implement basic configuration loading
3. Add handbook state tracking
4. Create Harmony patches for handbook GUI
5. Test handbook memory functionality

**Learning Focus:**
- ModSystem basics and lifecycle
- Configuration file handling
- Simple Harmony patching
- UI state management

### Phase 2: Stealth System
**Goal**: Implement realistic stealth mechanics with AI memory
**Complexity**: ⭐⭐⭐⭐ (Advanced)
**Duration**: 3-5 days

**Tasks:**
1. Research entity detection in VintageStoryAPI
2. Implement line-of-sight raycasting
3. Create AI memory management system
4. Patch entity detection methods with Harmony
5. Add performance optimizations and cleanup
6. Extensive testing with different entity types

**Learning Focus:**
- Advanced Harmony patching techniques
- Game AI and entity systems
- Performance optimization
- Memory management
- Vector mathematics and raycasting

### Phase 3: Advanced Configuration
**Goal**: Integrate ConfigLib and add advanced configuration features
**Complexity**: ⭐⭐⭐ (Intermediate)
**Duration**: 2-3 days

**Tasks:**
1. Add ConfigLib dependency
2. Create modern GUI configuration interface
3. Implement live reload system
4. Add configuration profiles and validation
5. Test cross-module configuration integration

**Learning Focus:**
- Third-party library integration
- Advanced configuration patterns
- GUI development concepts
- Data validation and sanitization

---

## Code Documentation Standards

### For Junior Developers
Every file must include comprehensive comments explaining:

#### 1. **File Purpose and Context**
```csharp
/// <summary>
/// This file handles [specific functionality] for the [module name] module.
/// 
/// For beginners: [Brief explanation of what this concept is and why it exists]
/// 
/// Example: This file manages AI memory - when entities remember where they 
/// last saw a player. Think of it like giving each creature a notepad to 
/// write down "I saw the player here at this time."
/// </summary>
```

#### 2. **Class and Method Documentation**
```csharp
/// <summary>
/// [What this class/method does]
/// 
/// For beginners: [Explain the concept in simple terms]
/// [Explain when and why you'd use this]
/// </summary>
/// <param name="paramName">[What this parameter is for]</param>
/// <returns>[What this method gives back]</returns>
```

#### 3. **Complex Logic Explanation**
```csharp
// For beginners: [Step-by-step explanation of what's happening]
// 1. [First step and why]
// 2. [Second step and why]
// Example: We're checking every 0.5 blocks because checking every 
// single coordinate would be too slow for the game.
```

#### 4. **Architecture Concepts**
- Explain design patterns (Singleton, Observer, etc.)
- Describe Vintage Story API concepts (ModSystem, Entity, World, etc.)
- Clarify C# concepts (static vs instance, properties vs fields, etc.)
- Explain Harmony patching concepts and when to use each type

#### 5. **Performance Notes**
```csharp
// Performance Note: [Why this approach was chosen]
// For beginners: [Explain the trade-off in simple terms]
```

---

## Configuration Schema

### Base Configuration Structure
```json
{
  "AemonsEssentials": {
    "handbook": {
      "enabled": true,
      "rememberLastPage": true,
      "maxStoredPages": 10
    },
    "stealth": {
      "enabled": true,
      "sneakDetectionMultiplier": 0.5,
      "enableLineOfSight": true,
      "aiMemoryDurationSeconds": 5.0,
      "debugLogging": false,
      "performanceMode": false
    },
    "configuration": {
      "useConfigLib": true,
      "enableLiveReload": true,
      "enableProfiles": false
    }
  }
}
```

### ConfigLib Integration Schema
```csharp
[ConfigLib.Group("Handbook Settings")]
public class HandbookConfig
{
    [ConfigLib.Slider(0f, 1f, 0.1f)]
    [ConfigLib.Tooltip("How much to reduce detection when sneaking")]
    public float sneakMultiplier { get; set; } = 0.5f;
    
    // ... etc
}
```

---

## Testing Strategy

### Module Testing
1. **Unit Tests**: Core logic functions (line-of-sight, memory management)
2. **Integration Tests**: Module interactions and configuration loading
3. **Performance Tests**: Memory usage and frame rate impact
4. **Compatibility Tests**: With popular mods and different game versions

### User Testing Scenarios
1. **Handbook**: Open via hotkey, via item hover, verify correct behavior
2. **Stealth**: Test sneaking detection, line-of-sight blocking, AI memory
3. **Configuration**: Change settings, verify live reload, test validation

---

## File Structure Overview
```
AemonsEssentials/
├── source/
│   ├── Common/
│   │   ├── ModSystemBase.cs           // Shared mod system functionality
│   │   ├── ConfigurationBase.cs       // Base configuration handling
│   │   └── Utils/
│   │       ├── LoggingUtils.cs        // Standardized logging
│   │       └── HarmonyUtils.cs        // Harmony helper methods
│   ├── HandbookMemory/               // Module 1: Smart Handbook
│   ├── StealthSystem/                // Module 2: Advanced Stealth  
│   └── ConfigurationSystem/          // Module 3: Unified Configuration
├── modinfo.json                      // Mod metadata
├── modconfig.json                    // Default configuration
└── README.md                         // User documentation
```

---

## Success Criteria

### Phase 1 Success
- [x] Handbook remembers last page when manually opened
- [x] Configuration system working
- [x] No conflicts with vanilla handbook behavior
- [x] Comprehensive beginner documentation

### Phase 2 Success  
- [x] Sneaking reduces detection by configured amount
- [x] Solid blocks prevent entity detection
- [x] AI remembers and pursues to last known location
- [x] Performance impact < 5% in normal gameplay
- [x] Works with common entity types (animals, monsters)

### Phase 3 Success
- [x] ConfigLib integration functional
- [x] Live configuration reload working
- [x] All settings accessible via modern GUI
- [x] Configuration validation prevents crashes
- [x] Cross-module configuration consistency

---

## Implementation Notes

### VintageStoryAPI References
- Located in `VintageStoryAPI/` folder (git submodule)
- Use for official API documentation and examples
- Reference when implementing entity detection, GUI systems, etc.

### Development Approach
- **Incremental**: Build one module at a time
- **Documented**: Every file thoroughly commented for beginners
- **Tested**: Each module tested independently before integration
- **Configurable**: All behavior customizable via configuration
- **Performance-Conscious**: Minimize game impact through efficient algorithms

This specification provides a complete roadmap for creating a professional, beginner-friendly, multi-module Vintage Story mod with advanced stealth mechanics, handbook improvements, and modern configuration management.
