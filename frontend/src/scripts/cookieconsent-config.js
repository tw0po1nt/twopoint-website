import 'https://cdn.jsdelivr.net/gh/orestbida/cookieconsent@3.0.1/dist/cookieconsent.umd.js';

// Enable dark mode
document.documentElement.classList.add('cc--darkmode');

CookieConsent.run({
    guiOptions: {
        consentModal: {
            layout: "box",
            position: "bottom left",
            equalWeightButtons: true,
            flipButtons: false
        },
        preferencesModal: {
            layout: "box",
            position: "right",
            equalWeightButtons: true,
            flipButtons: false
        }
    },
    categories: {
        necessary: {
            readOnly: true
        },
        analytics: {}
    },
    language: {
        default: "en",
        autoDetect: "browser",
        translations: {
            en: {
                consentModal: {
                    title: "Gimme cookie, got you cookie!",
                    description: "We use cookies primarily for analytics to enhance your experience. By accepting, you agree to our use of these cookies. You can manage your preferences or learn more about our cookie policy.",
                    acceptAllBtn: "Accept all",
                    acceptNecessaryBtn: "Reject all",
                    showPreferencesBtn: "Manage preferences",
                    footer: "<a href=\"/privacy\">Privacy Policy</a>"
                },
                preferencesModal: {
                    title: "Consent Preferences Center",
                    acceptAllBtn: "Accept all",
                    acceptNecessaryBtn: "Reject all",
                    savePreferencesBtn: "Save preferences",
                    closeIconLabel: "Close modal",
                    serviceCounterLabel: "Service|Services",
                    sections: [
                        {
                            title: "Understanding Cookies",
                            description: "Cookies are small data files used to store information on your device. They help us improve our site and your experience."
                        },
                        {
                            title: "Strictly Necessary Cookies <span class=\"pm__badge\">Always Enabled</span>",
                            description: "These cookies are essential for the operation of our site. They don't collect personal data and are necessary for features like accessing secure areas.",
                            linkedCategory: "necessary"
                        },
                        {
                            title: "Analytics Cookies",
                            description: "We use analytics cookies to understand how visitors interact with our website. This helps us to improve the user experience and the services we offer.",
                            linkedCategory: "analytics"
                        },
                        {
                            title: "More information",
                            description: "For any query in relation to my policy on cookies and your choices, please <a class=\"cc__link\" href=\"mailto:matt@twopoint.dev\">contact me</a>."
                        }
                    ]
                }
            },
            es: {
                consentModal: {
                    title: "Hola viajero, es la hora de las galletas!",
                    description: "Utilizamos cookies principalmente con fines analíticos para mejorar tu experiencia. Al aceptar, das tu consentimiento para que utilicemos estas cookies. Puedes gestionar tus preferencias o conocer más sobre nuestra política de cookies.",
                    acceptAllBtn: "Aceptar todo",
                    acceptNecessaryBtn: "Rechazar todo",
                    showPreferencesBtn: "Gestionar preferencias",
                    footer: "<a href=\"/privacy\">Política de privacidad</a>"
                },
                preferencesModal: {
                    title: "Preferencias de Consentimiento",
                    acceptAllBtn: "Aceptar todo",
                    acceptNecessaryBtn: "Rechazar todo",
                    savePreferencesBtn: "Guardar preferencias",
                    closeIconLabel: "Cerrar modal",
                    serviceCounterLabel: "Servicios",
                    sections: [
                        {
                            title: "Entendiendo las Cookies",
                            description: "Las cookies son pequeños archivos de datos que se utilizan para almacenar información en su dispositivo. Nos ayudan a mejorar nuestro sitio y su experiencia."
                        },
                        {
                            title: "Cookies Estrictamente Necesarias <span class=\"pm__badge\">Siempre Habilitado</span>",
                            description: "Estas cookies son esenciales para el funcionamiento de nuestro sitio. No recopilan datos personales y son necesarias para funciones como el acceso a áreas seguras.",
                            linkedCategory: "necessary"
                        },
                        {
                            title: "Cookies Analíticas",
                            description: "Utilizamos cookies analíticas para comprender cómo interactúan los visitantes con nuestro sitio web. Esto nos ayuda a mejorar la experiencia del usuario y los servicios que ofrecemos.",
                            linkedCategory: "analytics"
                        },
                        {
                            title: "Más información",
                            description: "Para cualquier consulta en relación con mi política de cookies y sus opciones, por favor póngase en <a class=\"cc__link\" href=\"mailto:matt@twopoint.dev\">contacto conmigo</a>."
                        }
                    ]
                }
            }
        }
    }
});