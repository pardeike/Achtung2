﻿using System;
using System.Collections.Generic;

namespace AchtungMod;

public class QuotaCache<S, T>(int maxRetrievals)
{
	private readonly Dictionary<S, (T value, int count)> cache = [];
	private readonly int maxRetrievals = maxRetrievals;

	private void Add(S key, T value) => cache[key] = (value, 0);

	public T Get(S key, Func<S, T> fetchCallback)
	{
		if (cache.ContainsKey(key) == false || cache[key].count >= maxRetrievals)
			Add(key, fetchCallback(key));

		var (value, count) = cache[key];
		cache[key] = (value, count + 1);

		return value;
	}
}