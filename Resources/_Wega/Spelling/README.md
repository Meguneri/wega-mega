Словарь русских словоформ: https://github.com/danakt/russian-words
Версия: d68c007fe9260d2d40ce413f13f77335c5af1e62, лицензия MIT (LICENSE.txt).

russian.bin — отсортированные 64-битные FNV-1a отпечатки словоформ, с приведением
к нижнему регистру и заменой ё на е. Первые 4 байта — число записей (int32 LE),
далее uint64 LE. Коллизия может только пропустить ошибку, но не отвергнуть слово.
Генерация: python dev/generate_chat_dictionary.py PATH_TO_RUSSIAN_TXT
Исходный russian.txt имеет кодировку windows-1251.
