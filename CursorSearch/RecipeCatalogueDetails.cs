using System;
using System.Collections;
using System.Reflection;
using Terraria;
using Terraria.UI;

namespace RecipeBrowserJPChatSearch
{
	/// <summary>Keep Recipe Browser's selected recipe in step with an RBJ query.</summary>
	internal sealed class RecipeCatalogueDetails
	{
		private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

		private readonly FieldInfo _recipeGrid;
		private readonly FieldInfo _rows;
		private readonly FieldInfo _slotIndex;
		private readonly FieldInfo _slotSelected;
		private readonly FieldInfo _selectedIndex;
		private readonly FieldInfo _recipeSlots;
		private readonly FieldInfo _recipeInfo;
		private readonly FieldInfo _ingredients;
		private readonly FieldInfo _duplicateButton;
		private readonly MethodInfo _updateGrid;
		private readonly MethodInfo _setRecipe;
		private readonly MethodInfo _clearIngredients;

		private RecipeCatalogueDetails(Type catalogue)
		{
			_recipeGrid = catalogue.GetField("recipeGrid", Instance);
			_rows = _recipeGrid?.FieldType.GetField("_items", Instance);
			_selectedIndex = catalogue.GetField("selectedIndex", Instance);
			_recipeSlots = catalogue.GetField("recipeSlots", Instance);
			Type slot = _recipeSlots?.FieldType.IsGenericType == true
				? _recipeSlots.FieldType.GetGenericArguments()[0] : null;
			_slotIndex = slot?.GetField("index", Instance);
			_slotSelected = slot?.GetField("selected", Instance);
			_recipeInfo = catalogue.GetField("recipeInfo", Instance);
			_ingredients = _recipeInfo?.FieldType.GetField("craftingIngredientsGrid", Instance);
			_duplicateButton = catalogue.GetField("duplicationButton", Instance);
			_updateGrid = catalogue.GetMethod("UpdateGrid", Instance, null, Type.EmptyTypes, null);
			_setRecipe = catalogue.GetMethod("SetRecipe", Instance, null, new[] { typeof(int) }, null);
			_clearIngredients = _ingredients?.FieldType.GetMethod("Clear", Instance, null, Type.EmptyTypes, null);
		}

		internal static RecipeCatalogueDetails TryCreate(Type catalogue)
		{
			RecipeCatalogueDetails binding;
			try
			{
				binding = new RecipeCatalogueDetails(catalogue);
			}
			catch (AmbiguousMatchException)
			{
				return null;
			}
			catch (TypeLoadException)
			{
				return null;
			}

			return binding._selectedIndex?.FieldType == typeof(int)
				&& !binding._selectedIndex.IsInitOnly
				&& binding._slotIndex?.FieldType == typeof(int)
				&& binding._slotSelected?.FieldType == typeof(bool)
				&& !binding._slotSelected.IsInitOnly
				&& binding._rows != null && typeof(IList).IsAssignableFrom(binding._rows.FieldType)
				&& typeof(IList).IsAssignableFrom(binding._recipeSlots.FieldType)
				&& binding._duplicateButton != null && !binding._duplicateButton.IsInitOnly
				&& typeof(UIElement).IsAssignableFrom(binding._duplicateButton.FieldType)
				&& binding._updateGrid?.ReturnType == typeof(void)
				&& binding._setRecipe?.ReturnType == typeof(void)
				&& binding._clearIngredients?.ReturnType == typeof(void)
				? binding : null;
		}

		internal void Synchronize(object catalogue, int queryType, Recipe[] recipes)
		{
			if (recipes == null || _recipeGrid.GetValue(catalogue) is not object grid
				|| _recipeSlots.GetValue(catalogue) is not IList slots
				|| _recipeInfo.GetValue(catalogue) is not object info
				|| _ingredients.GetValue(info) is not object ingredients)
				return;

			// Run the same refresh Recipe Browser would run on its next update.
			// The visible list already accounts for Recipe Browser's filters and ordering.
			_updateGrid.Invoke(catalogue, null);
			if (_rows.GetValue(grid) is not IList rows)
				return;

			int previous = (int)_selectedIndex.GetValue(catalogue);
			int match = -1;
			foreach (object row in rows)
			{
				if (row == null || !_slotIndex.DeclaringType.IsInstanceOfType(row))
					continue;

				int index = (int)_slotIndex.GetValue(row);
				if (index < 0 || index >= recipes.Length || index >= slots.Count)
					continue;

				// The list also includes recipes that use the query item. Only its own recipe supplies the details.
				if (recipes[index]?.createItem?.type != queryType)
					continue;

				if (index == previous)
				{
					match = index;
					break;
				}
				if (match < 0)
					match = index;
			}

			if (match == previous)
				return;

			if (match >= 0)
			{
				_setRecipe.Invoke(catalogue, new object[] { match });
				return;
			}

			// No matching visible recipe: remove details of the obsolete selection.
			_selectedIndex.SetValue(catalogue, -1);
			if (previous >= 0 && previous < slots.Count && slots[previous] is object previousSlot)
				_slotSelected.SetValue(previousSlot, false);
			_clearIngredients.Invoke(ingredients, null);
			if (_duplicateButton.GetValue(catalogue) is UIElement button)
			{
				button.Remove();
				_duplicateButton.SetValue(catalogue, null);
			}
		}

	}
}
