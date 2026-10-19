# Публикация на GitHub

## 1. Создать репозиторий

На GitHub: **New repository** → имя `BgClicker` → **Public** → **без** README, LICENSE
и .gitignore (они уже есть в проекте).

## 2. Загрузить код

Установите [Git for Windows](https://git-scm.com/download/win), откройте PowerShell в папке
проекта (там, где лежит `build.ps1`) и выполните:

```powershell
git init -b main
git add .
git commit -m "BgClicker 1.1.0"
git remote add origin https://github.com/Me1p/BgClicker.git
git push -u origin main
```

Если Git ещё не настроен:

```powershell
git config --global user.name "Ваше имя"
git config --global user.email "mail@example.com"
```

Без Git можно загрузить файлы через веб-интерфейс GitHub (**Add file → Upload files**),
но тогда `.github/workflows` и `.gitignore` нужно добавить вручную.

## 3. Проверить сборку

После push откройте вкладку **Actions** → workflow **Build**. Он соберёт exe и выложит
артефакт `BgClicker`.

## 4. Выпустить релиз

```powershell
git tag v1.1.0
git push origin v1.1.0
```

Workflow **Release** соберёт файлы и создаст GitHub Release с exe, конфигом, README,
лицензией и `SHA256SUMS.txt`.

## 5. Подпись (по желанию, убирает SmartScreen)

Добавьте в **Settings → Secrets and variables → Actions**:

- `WINDOWS_CERT_PFX_BASE64` — `.pfx` в base64;
- `WINDOWS_CERT_PASSWORD` — пароль.

Подробности: [docs/SMARTSCREEN.md](docs/SMARTSCREEN.md).

## 6. Что не попадает в репозиторий

`dist/`, `bin/`, `obj/`, профили Chrome, логи и настройки — перечислены в `.gitignore`.
Готовые exe публикуются через GitHub Releases, а не коммитятся в git.
