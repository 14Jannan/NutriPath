using NutriPath.Api.Services;

namespace NutriPath.Api.Tests;

// Tests the real NutrientScaling that MealService uses to snapshot a
// logged portion's nutrients.
public class NutrientScalingTests
{
    [Fact]
    public void Scale_DoublePortion_DoublesValue()
    {
        Assert.Equal(400m, NutrientScaling.Scale(perServing: 200, servingSizeGrams: 100, quantityGrams: 200));
    }

    [Fact]
    public void Scale_HalfPortion_HalvesValue()
    {
        Assert.Equal(178m, NutrientScaling.Scale(perServing: 356, servingSizeGrams: 100, quantityGrams: 50));
    }

    [Fact]
    public void Scale_SamePortionAsServingSize_ReturnsUnchanged()
    {
        Assert.Equal(139m, NutrientScaling.Scale(perServing: 139, servingSizeGrams: 100, quantityGrams: 100));
    }

    [Fact]
    public void Scale_RoundsToOneDecimalPlace()
    {
        // 130 * 33 / 100 = 42.9; 7 * 33 / 100 = 2.31 -> 2.3
        Assert.Equal(42.9m, NutrientScaling.Scale(perServing: 130, servingSizeGrams: 100, quantityGrams: 33));
        Assert.Equal(2.3m, NutrientScaling.Scale(perServing: 7, servingSizeGrams: 100, quantityGrams: 33));
    }

    [Fact]
    public void Scale_ZeroServingSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NutrientScaling.Scale(perServing: 100, servingSizeGrams: 0, quantityGrams: 50));
    }
}
