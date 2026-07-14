# Changelog

## [1.3.0]
— Так как экстеншены и интерфейс ILogger были вынесены из Infrastructure в отдельные пакеты, то появились ссылки на эти пакеты.

## [1.2.8]
- PlayerPrefs: убраны лишние вызовы OnChanged из ForceSave
- PlayerPrefs: добавлены методы Track/Untrack для унификации с Observable
- Observable: ивент Changed переименован в OnChanged для унификации с PlayerPrefs (Changed помечен Obsolete)

## [1.2.6]
- DateTimeValue можно задавать как дату, без времени

## [1.2.5]
- Исправлена компиляция на Десктоп платформе

## [1.2.3]
- GameConfig - добавлена поооерка путей для AssetDatabase
- 
## [1.2.2]
- Добавлен список изменений
- Переделан ConfigSwitcher - настройка хранится в UserSettings (не сбрасывается чисткой PlayerPrefs)
- Настройки Local/Remote и версия конфига вынесены в тулбар юнити
- Атрибут для конфигов - GameConfig. Задает путь к конфигу и игре и в редакторе
- Список конфигов в тулбаре юнити

## [1.2.1]
- DateTimeValue можно использовать как UTC или локальное время

## [1.2.0]
- Переделан класс ConfigWithJson, удалена зависимость от SayKitApp

**ConfigWithJson.InitWithRemote теперь принимает поле из конфига вместо всего SayKitGameConfig.
 Функция GetJsonFromRemote больше не нужна**

## [1.1.5]
- Фикс ConstantValuesAttributeDrawer
- Доработан RecentAssetsCycler

## [1.1.4]
- Отрицательные интервалы в TimeSpanValue

## [1.1.3]
- Добавлен класс TimeOfDayValue

## [1.1.2]
- Фикс загрузки ConfigWithJson в редакторе

## [1.1.0]
- Переделаны PlayerPrefs добавлен PlayerPrefsCollection и бэкенды

**Перед созданием первого PlayerPrefs надо проинициализировать, например так**

    PlayerPrefsCollection.Init(new PrefsStorageBackend());

## [1.0.6]
- Добавлен интерфейс ILogger

## [1.0.3]
- Добавлен корневой неймспейс SGS.Infrastructure

## [1.0.2]
- Обновлено LocalizationFontsWindow для совместимости с новыми локализациями в SayKit 

## [1.0.1] 
- Добавлены функции в VectorExtensions
- Добавлена RectTransformData
- Кнопки на тулбаре - UnityToolbarExtender
- Переключение ассетов по ^Tab -  RecentAssetsCycler

## [1.0.0] 

### Код вынесен из CiolorSlide 
