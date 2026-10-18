using System;
using UnityEngine;

namespace PolyCar
{
    [CreateAssetMenu(menuName = "Poly Car/Car Catalog")]
    public class CarCatalog : ScriptableObject
    {
        public CarSpec[] cars = Array.Empty<CarSpec>();

        private static CarCatalog _instance;
        public static CarCatalog Instance => _instance ? _instance : (_instance = Resources.Load<CarCatalog>("CarCatalog"));

        public CarSpec Get(string id)
        {
            foreach (var c in cars) if (c.id == id) return c;
            return cars.Length > 0 ? cars[0] : null;
        }

        public int IndexOf(string id)
        {
            for (int i = 0; i < cars.Length; i++) if (cars[i].id == id) return i;
            return 0;
        }
    }
}
