global using SimpleBlueprint = Kingmaker.Blueprints.BlueprintScriptableObject;
using Kingmaker.EntitySystem.Entities;

namespace BuffIt2TheLimit {

    // Kingmaker has a single pet per unit (the animal companion); WotR has typed pets.
    public enum PetType { AnimalCompanion }

    static class KmPets {
        public static UnitEntityData GetPet(this UnitEntityData unit, PetType type) => unit.Descriptor.Pet;
    }
}
