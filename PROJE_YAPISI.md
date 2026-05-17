# Çoklu Tarayıcı (BrowserView) Proje Yapısı

Bu doküman, "Fail2Ban.UI" benzeri ancak çoklu tarayıcı yönetimi odaklı yeni bir projenin mimarisini ve bir yapay zekanın (AI) bu projeyi nasıl geliştirmesi gerektiğini tanımlar.

## 1. Proje Mimarisi

Proje üç ana katmandan oluşacaktır:

### A. Frontend (UI) - `Fail2Ban.UI`
- **Teknoloji:** Vite + React (veya Vue) + Tailwind CSS.
- **Masaüstü Katmanı:** Electron.
- **Özellikler:** 
  - Modern ve "Premium" tasarım (Glassmorphism, Dark Mode).
  - BrowserView yönetimi (Sekme sistemi, çoklu pencere kontrolü).
  - .NET API ile Auth entegrasyonu.

### B. Backend (API) - `Fail2Ban.API`
- **Teknoloji:** .NET Core 8 Web API.
- **Güvenlik:** JWT Bearer Authentication + Role Based Access Control (RBAC).
- **Veritabanı:** Redis (Session & Log yönetimi).

---

## 2. Dizin Yapısı

```text
/
├── Fail2Ban.UI/                # Frontend & Electron
│   ├── src/                    # React/Vue Kodları
│   ├── electron/               # Main process (BrowserView logic)
│   └── tailwind.config.js
├── Fail2Ban.API/               # .NET Core API
└── docs/                       # AI Talimatları ve Akışlar
    ├── workflow.md
    └── skills.md
```
