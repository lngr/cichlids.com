/******/ (function(modules) { // webpackBootstrap
/******/ 	// The module cache
/******/ 	var installedModules = {};

/******/ 	// The require function
/******/ 	function __webpack_require__(moduleId) {

/******/ 		// Check if module is in cache
/******/ 		if(installedModules[moduleId])
/******/ 			return installedModules[moduleId].exports;

/******/ 		// Create a new module (and put it into the cache)
/******/ 		var module = installedModules[moduleId] = {
/******/ 			exports: {},
/******/ 			id: moduleId,
/******/ 			loaded: false
/******/ 		};

/******/ 		// Execute the module function
/******/ 		modules[moduleId].call(module.exports, module, module.exports, __webpack_require__);

/******/ 		// Flag the module as loaded
/******/ 		module.loaded = true;

/******/ 		// Return the exports of the module
/******/ 		return module.exports;
/******/ 	}


/******/ 	// expose the modules object (__webpack_modules__)
/******/ 	__webpack_require__.m = modules;

/******/ 	// expose the module cache
/******/ 	__webpack_require__.c = installedModules;

/******/ 	// __webpack_public_path__
/******/ 	__webpack_require__.p = "";

/******/ 	// Load entry module and return exports
/******/ 	return __webpack_require__(0);
/******/ })
/************************************************************************/
/******/ ([
/* 0 */
/***/ function(module, exports, __webpack_require__) {

	/* WEBPACK VAR INJECTION */(function(__dirname) {"use strict";
	var path = __webpack_require__(38);
	var express = __webpack_require__(37);
	__webpack_require__(35);
	var angular2_universal_preview_1 = __webpack_require__(34);
	var core_1 = __webpack_require__(1);
	var router_1 = __webpack_require__(2);
	var app_component_1 = __webpack_require__(22);
	var app = express();
	var root = path.join(path.resolve(__dirname, '../dist/client'));
	core_1.enableProdMode();
	app.engine('.html', angular2_universal_preview_1.expressEngine);
	app.set('views', root);
	app.set('view engine', 'html');
	function ngApp(req, res) {
	    var baseUrl = '/';
	    var url = req.originalUrl || '/';
	    res.render('index', {
	        directives: [
	            app_component_1.AppComponent,
	        ],
	        providers: [
	            core_1.provide(router_1.APP_BASE_HREF, { useValue: baseUrl }),
	            core_1.provide(angular2_universal_preview_1.REQUEST_URL, { useValue: url }),
	            router_1.ROUTER_PROVIDERS,
	            angular2_universal_preview_1.NODE_LOCATION_PROVIDERS,
	            angular2_universal_preview_1.NODE_PRELOAD_CACHE_HTTP_PROVIDERS,
	        ],
	        async: true,
	        precache: true,
	        preboot: true,
	    });
	}
	app.use(express.static(root, { index: false }));
	app.use('/', ngApp);
	// Server
	app.listen(3001, function () {
	    console.log('Listen on http://localhost:3001');
	});

	/* WEBPACK VAR INJECTION */}.call(exports, "src"))

/***/ },
/* 1 */
/***/ function(module, exports) {

	module.exports = require("angular2/core");

/***/ },
/* 2 */
/***/ function(module, exports) {

	module.exports = require("angular2/router");

/***/ },
/* 3 */
/***/ function(module, exports, __webpack_require__) {

	"use strict";
	var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
	    var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
	    if (typeof Reflect === "object" && typeof Reflect.decorate === "function") r = Reflect.decorate(decorators, target, key, desc);
	    else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
	    return c > 3 && r && Object.defineProperty(target, key, r), r;
	};
	var __metadata = (this && this.__metadata) || function (k, v) {
	    if (typeof Reflect === "object" && typeof Reflect.metadata === "function") return Reflect.metadata(k, v);
	};
	var core_1 = __webpack_require__(1);
	var http_1 = __webpack_require__(11);
	var Observable_1 = __webpack_require__(12);
	var picture_config_service_1 = __webpack_require__(4);
	var PictureService = (function () {
	    function PictureService(_config, _http) {
	        this._config = _config;
	        this._http = _http;
	    }
	    PictureService.prototype.listPictures = function (query) {
	        var params = new http_1.URLSearchParams();
	        params.set('sort', query.sort || "newest");
	        params.set('offset', "" + (query.offset || 0));
	        params.set('count', "" + query.count);
	        return this._http
	            .get(this._config.getListURI(), {
	            search: params,
	        })
	            .map(function (res) { return res.json(); })
	            .catch(this.handleError);
	    };
	    PictureService.prototype.viewPicture = function (slug) {
	        return this._http.get(this._config.getViewURI(slug))
	            .map(function (res) { return res.json(); })
	            .catch(this.handleError);
	    };
	    PictureService.prototype.handleError = function (error) {
	        // in a real world app, we may send the error to some remote logging infrastructure
	        // instead of just logging it to the console
	        console.error(error);
	        return Observable_1.Observable.throw(error.json().error || 'Server error');
	    };
	    PictureService = __decorate([
	        core_1.Injectable(), 
	        __metadata('design:paramtypes', [picture_config_service_1.PictureConfigService, http_1.Http])
	    ], PictureService);
	    return PictureService;
	}());
	exports.PictureService = PictureService;


/***/ },
/* 4 */
/***/ function(module, exports, __webpack_require__) {

	"use strict";
	var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
	    var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
	    if (typeof Reflect === "object" && typeof Reflect.decorate === "function") r = Reflect.decorate(decorators, target, key, desc);
	    else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
	    return c > 3 && r && Object.defineProperty(target, key, r), r;
	};
	var __metadata = (this && this.__metadata) || function (k, v) {
	    if (typeof Reflect === "object" && typeof Reflect.metadata === "function") return Reflect.metadata(k, v);
	};
	var core_1 = __webpack_require__(1);
	var Config = __webpack_require__(6);
	var PICTURES_BASE_URI = Config.API_BASE_URL + "pictures";
	var PictureConfigService = (function () {
	    function PictureConfigService() {
	    }
	    PictureConfigService.prototype.getBaseURI = function () {
	        return PICTURES_BASE_URI;
	    };
	    PictureConfigService.prototype.getListURI = function () {
	        return this.getBaseURI();
	    };
	    PictureConfigService.prototype.getViewURI = function (slug) {
	        return this.getBaseURI() + "/" + encodeURI(slug);
	    };
	    PictureConfigService = __decorate([
	        core_1.Injectable(), 
	        __metadata('design:paramtypes', [])
	    ], PictureConfigService);
	    return PictureConfigService;
	}());
	exports.PictureConfigService = PictureConfigService;


/***/ },
/* 5 */
/***/ function(module, exports, __webpack_require__) {

	"use strict";
	var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
	    var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
	    if (typeof Reflect === "object" && typeof Reflect.decorate === "function") r = Reflect.decorate(decorators, target, key, desc);
	    else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
	    return c > 3 && r && Object.defineProperty(target, key, r), r;
	};
	var __metadata = (this && this.__metadata) || function (k, v) {
	    if (typeof Reflect === "object" && typeof Reflect.metadata === "function") return Reflect.metadata(k, v);
	};
	var core_1 = __webpack_require__(1);
	var router_1 = __webpack_require__(2);
	var UserProfileLink = (function () {
	    function UserProfileLink(_router, _location, _el) {
	        this._router = _router;
	        this._location = _location;
	        this._el = _el;
	    }
	    UserProfileLink.prototype.setUrl = function (url) {
	        this._el.nativeElement.href = url;
	    };
	    Object.defineProperty(UserProfileLink.prototype, "userProfileLink", {
	        set: function (user) {
	            if (!user) {
	                this.setUrl("#");
	            }
	            else {
	                // const instr = this._router.generate([]
	                //var navigationHref = this._navigationInstruction.toLinkUrl();
	                // this.visibleHref = this._location.prepareExternalUrl(navigationHref);
	                this.setUrl("/users/" + encodeURI(user.username));
	            }
	        },
	        enumerable: true,
	        configurable: true
	    });
	    __decorate([
	        core_1.Input(), 
	        __metadata('design:type', Object), 
	        __metadata('design:paramtypes', [Object])
	    ], UserProfileLink.prototype, "userProfileLink", null);
	    UserProfileLink = __decorate([
	        core_1.Directive({
	            selector: '[userProfileLink]',
	        }), 
	        __metadata('design:paramtypes', [router_1.Router, router_1.Location, core_1.ElementRef])
	    ], UserProfileLink);
	    return UserProfileLink;
	}());
	exports.UserProfileLink = UserProfileLink;


/***/ },
/* 6 */
/***/ function(module, exports) {

	"use strict";
	// export const API_BASE_URL   = ('development' === ENV) ? "http://www.cichlids.com/api/" : "/api/";
	exports.API_BASE_URL = "http://www.cichlids.com/api/";


/***/ },
/* 7 */
/***/ function(module, exports, __webpack_require__) {

	"use strict";
	var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
	    var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
	    if (typeof Reflect === "object" && typeof Reflect.decorate === "function") r = Reflect.decorate(decorators, target, key, desc);
	    else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
	    return c > 3 && r && Object.defineProperty(target, key, r), r;
	};
	var __metadata = (this && this.__metadata) || function (k, v) {
	    if (typeof Reflect === "object" && typeof Reflect.metadata === "function") return Reflect.metadata(k, v);
	};
	var core_1 = __webpack_require__(1);
	var http_1 = __webpack_require__(11);
	var Observable_1 = __webpack_require__(12);
	var CommentService = (function () {
	    function CommentService(_http) {
	        this._http = _http;
	    }
	    CommentService.prototype.listCommentsFromUrl = function (url) {
	        return this._http
	            .get(url)
	            .map(function (res) { return res.json(); })
	            .catch(this.handleError);
	    };
	    CommentService.prototype.handleError = function (error) {
	        // in a real world app, we may send the error to some remote logging infrastructure
	        // instead of just logging it to the console
	        console.error(error);
	        return Observable_1.Observable.throw(error.json().error || 'Server error');
	    };
	    CommentService = __decorate([
	        core_1.Injectable(), 
	        __metadata('design:paramtypes', [http_1.Http])
	    ], CommentService);
	    return CommentService;
	}());
	exports.CommentService = CommentService;


/***/ },
/* 8 */
/***/ function(module, exports, __webpack_require__) {

	"use strict";
	var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
	    var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
	    if (typeof Reflect === "object" && typeof Reflect.decorate === "function") r = Reflect.decorate(decorators, target, key, desc);
	    else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
	    return c > 3 && r && Object.defineProperty(target, key, r), r;
	};
	var __metadata = (this && this.__metadata) || function (k, v) {
	    if (typeof Reflect === "object" && typeof Reflect.metadata === "function") return Reflect.metadata(k, v);
	};
	var core_1 = __webpack_require__(1);
	var DynImgComponent = (function () {
	    function DynImgComponent() {
	        this.sizes = {};
	    }
	    Object.defineProperty(DynImgComponent.prototype, "src", {
	        get: function () {
	            return this.getSrc();
	        },
	        enumerable: true,
	        configurable: true
	    });
	    DynImgComponent.prototype.availSizes = function () {
	        if (!this.sizes)
	            return [];
	        var keys = [];
	        for (var k in this.sizes) {
	            keys.push(+k);
	        }
	        keys.sort(function (a, b) { return b - a; });
	        return keys;
	    };
	    DynImgComponent.prototype.getSrc = function () {
	        var available = this.availSizes();
	        if (!available || !available.length)
	            return "";
	        var largest = available[0];
	        return this.sizes[largest];
	    };
	    Object.defineProperty(DynImgComponent.prototype, "srcSet", {
	        get: function () {
	            var _this = this;
	            return this.availSizes()
	                .map(function (s) { return _this.sizes[s] + " " + s + "w"; })
	                .join(",");
	        },
	        enumerable: true,
	        configurable: true
	    });
	    __decorate([
	        core_1.Input(), 
	        __metadata('design:type', Object)
	    ], DynImgComponent.prototype, "sizes", void 0);
	    __decorate([
	        core_1.Input(), 
	        __metadata('design:type', String)
	    ], DynImgComponent.prototype, "alt", void 0);
	    __decorate([
	        core_1.Input(), 
	        __metadata('design:type', String)
	    ], DynImgComponent.prototype, "styleClass", void 0);
	    DynImgComponent = __decorate([
	        core_1.Component({
	            selector: 'dyn-img',
	            template: "<img attr.src=\"{{src}}\" attr.srcset=\"{{srcSet}}\" attr.alt=\"{{alt}}\" class=\"{{styleClass}}\">",
	        }), 
	        __metadata('design:paramtypes', [])
	    ], DynImgComponent);
	    return DynImgComponent;
	}());
	exports.DynImgComponent = DynImgComponent;


/***/ },
/* 9 */
/***/ function(module, exports, __webpack_require__) {

	"use strict";
	var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
	    var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
	    if (typeof Reflect === "object" && typeof Reflect.decorate === "function") r = Reflect.decorate(decorators, target, key, desc);
	    else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
	    return c > 3 && r && Object.defineProperty(target, key, r), r;
	};
	var __metadata = (this && this.__metadata) || function (k, v) {
	    if (typeof Reflect === "object" && typeof Reflect.metadata === "function") return Reflect.metadata(k, v);
	};
	var core_1 = __webpack_require__(1);
	var dyn_img_component_1 = __webpack_require__(8);
	var PictureImgComponent = (function () {
	    function PictureImgComponent() {
	    }
	    __decorate([
	        core_1.Input(), 
	        __metadata('design:type', Object)
	    ], PictureImgComponent.prototype, "picture", void 0);
	    __decorate([
	        core_1.Input(), 
	        __metadata('design:type', String)
	    ], PictureImgComponent.prototype, "styleClass", void 0);
	    PictureImgComponent = __decorate([
	        core_1.Component({
	            selector: 'picture-img',
	            template: "<dyn-img [sizes]=\"picture?.image\" [alt]=\"picture?.title\" [class]=\"styleClass\">",
	            directives: [dyn_img_component_1.DynImgComponent],
	        }), 
	        __metadata('design:paramtypes', [])
	    ], PictureImgComponent);
	    return PictureImgComponent;
	}());
	exports.PictureImgComponent = PictureImgComponent;


/***/ },
/* 10 */
/***/ function(module, exports, __webpack_require__) {

	"use strict";
	var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
	    var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
	    if (typeof Reflect === "object" && typeof Reflect.decorate === "function") r = Reflect.decorate(decorators, target, key, desc);
	    else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
	    return c > 3 && r && Object.defineProperty(target, key, r), r;
	};
	var __metadata = (this && this.__metadata) || function (k, v) {
	    if (typeof Reflect === "object" && typeof Reflect.metadata === "function") return Reflect.metadata(k, v);
	};
	var core_1 = __webpack_require__(1);
	var picture_list_entry_component_1 = __webpack_require__(28);
	var PictureListComponent = (function () {
	    function PictureListComponent() {
	    }
	    __decorate([
	        core_1.Input(), 
	        __metadata('design:type', Array)
	    ], PictureListComponent.prototype, "pictures", void 0);
	    PictureListComponent = __decorate([
	        core_1.Component({
	            selector: 'picture-list',
	            template: __webpack_require__(19),
	            directives: [
	                picture_list_entry_component_1.PictureListEntryComponent,
	            ],
	        }), 
	        __metadata('design:paramtypes', [])
	    ], PictureListComponent);
	    return PictureListComponent;
	}());
	exports.PictureListComponent = PictureListComponent;


/***/ },
/* 11 */
/***/ function(module, exports) {

	module.exports = require("angular2/http");

/***/ },
/* 12 */
/***/ function(module, exports) {

	module.exports = require("rxjs/Observable");

/***/ },
/* 13 */
/***/ function(module, exports) {

	module.exports = "<nav class=\"navbar navbar-inverse navbar-static-top\" role=\"navigation\">\n    <div class=\"container\">\n        <div class=\"navbar-header\">\n            <button type=\"button\" class=\"navbar-toggle collapsed\" data-toggle=\"collapse\" data-target=\"#cichlids-top-navbar-collapse-1\" aria-expanded=\"false\">\n                <span class=\"sr-only\">Toggle navigation</span>\n                <span class=\"icon-bar\"></span>\n                <span class=\"icon-bar\"></span>\n                <span class=\"icon-bar\"></span>\n            </button>\n            <a class=\"navbar-brand\" [routerLink]=\"['/Frontpage']\">\n                <div class=\"navbar-logo pull-left\">\n                    <img src=\"/static/img/calvus-small.png\" style=\"\n                    position: relative;\n                                     top: -10px;\n                                     left: -5px;\n                                     height: 35px;\" />\n                </div>\n            </a>\n        </div>\n\n        <div class=\"navbar-collapse collapse\" id=\"cichlids-top-navbar-collapse-1\">\n            <ul class=\"nav navbar-nav\">\n                <li class=\"dropdown\">\n                    <a href=\"#\" class=\"dropdown-toggle\" data-toggle=\"dropdown\"\n                        role=\"button\" aria-haspopup=\"true\" aria-expanded=\"false\">\n                        Explore <span class=\"caret\"></span></a>\n                    <ul class=\"dropdown-menu\">\n                        <li><a [routerLink]=\"['/Pictures']\">Pictures</a></li>\n                        <!-- <li><a href=\"#\">Cichlids</a></li> -->\n                        <!-- <li><a href=\"#\">Videos</a></li> -->\n                        <!-- <li><a href=\"#\">Tanks</a></li> -->\n                        <!-- <li><a href=\"#\">Q &amp; A</a></li> -->\n                        <!-- <li><a href=\"#\">Users</a></li> -->\n                    </ul>\n                </li>\n        <!-- //        exploreMenu.addLink(new Model<String>(\"Highlights\"), UserProfilePage.class, null); -->\n        <!-- //        exploreMenu.addLink(new Model<String>(\"Galleries\"), UserProfilePage.class, null); -->\n        <!-- //        exploreMenu.addLink(new Model<String>(\"Users\"), UserProfilePage.class); -->\n        <!-- //        exploreMenu.addLink(new Model<String>(\"Tags\"), UserProfilePage.class, null); -->\n        <!-- //        exploreMenu.addLink(new Model<String>(\"Badges\"), UserProfilePage.class, null); -->\n        <!-- //        exploreMenu.addLink(new Model<String>(\"Photo Contest\"), UserProfilePage.class, null); -->\n                <!-- <li><a href=\"#\">Market</a></li> -->\n                <!-- <li><a href=\"#\">Gear</a></li> -->\n                <!-- <li><a href=\"#\">Guides</a></li> -->\n                <li><a href=\"http://www.cichlids.com/disc/\">Forum</a></li>\n            </ul>\n\n\n            <!-- <li><a href=\"#\">Highlights</a></li> -->\n            <!-- <li><a href=\"#\">Galleries</a></li> -->\n            <!-- <li><a href=\"#\">Tags</a></li> -->\n            <!-- <li><a href=\"#\">Badges</a></li> -->\n            <!-- <li><a href=\"#\">Photo Contest</a></li> -->\n\n            <!-- <li class=\"active\"><a href=\"#\">Pics</a></li> -->\n            <!-- <li><a href=\"#\">Tanks</a></li> -->\n            <!-- <li><a href=\"#\">Questions</a></li> -->\n            <!-- <li><a href=\"#\">Users</a></li> -->\n            <!-- <li class=\"dropdown\"> -->\n            <!-- <a href=\"explore.html\" class=\"dropdown-toggle\" data-toggle=\"dropdown\">Explore -->\n            <!-- <b class=\"caret\"></b> -->\n            <!-- </a> -->\n            <!-- <ul class=\"dropdown-menu\"> -->\n            <!-- <li><a href=\"#\">Highlights</a></li> -->\n            <!-- <li><a href=\"#\">Galleries</a></li> -->\n            <!-- <li><a href=\"#\">Tags</a></li> -->\n            <!-- <li><a href=\"#\">Badges</a></li> -->\n            <!-- <li><a href=\"#\">Photo Contest</a></li> -->\n            <!-- </ul> -->\n            <!-- </li> -->\n\n\n            <div class=\"btn-group navbar-right\">\n                <a href=\"#\" class=\"btn btn-primary navbar-btn\">\n                    Sign up\n                </a>\n            </div>\n\n            <ul class=\"nav navbar-nav navbar-right\">\n                <li><a href=\"#\"><i class=\"icon-white icon-user\"></i> Sign in</a></li>\n            </ul>\n\n            <ul class=\"nav navbar-nav navbar-right\">\n                <li>\n                    <a href=\"#\" class=\"dropdown-toggle\" style=\"padding: 10px 0px 0px 10px;\" data-toggle=\"dropdown\">\n                        <img src=\"http://placehold.it/30x30.png\" width=\"30\" height=\"30\" class=\"img-rounded hidden-xs\" />\n                        <span class=\"visible-xs\">My Account <span class=\"caret\"></span></span>\n                    </a>\n                    <ul class=\"dropdown-menu\">\n                        <li><a href=\"#\">My Profile</a></li>\n                        <li><a href=\"#\">Messages</a></li>\n                        <li><a href=\"#\">Settings</a></li>\n                        <li class=\"divider\"></li>\n                        <li><a href=\"#\">My Galleries</a></li>\n                        <li><a href=\"#\">My Pictures</a></li>\n                        <li class=\"divider\"></li>\n                        <li><a href=\"#\">Logout</a></li>\n                    </ul>\n                </li>\n            </ul>\n\n\n            <div class=\"btn-group navbar-right\">\n                <a href=\"#\" class=\"btn btn-primary navbar-btn dropdown-toggle uploadlink \" data-toggle=\"dropdown\">\n                    Upload <b class=\"caret\"></b>\n                </a>\n                <ul class=\"dropdown-menu\" role=\"menu\">\n                    <li><a href=\"#\" wicket:id=\"uploadpicture\">Upload Picture</a></li>\n                    <li><a href=\"#\" icket:id=\"newgallery\">New Gallery</a></li>\n                </ul>\n            </div>\n\n        </div>\n    </div>\n</nav>\n\n\n\n\n\n\n<div class=\"container\">\n    <router-outlet></router-outlet>\n</div>\n\n<div id=\"ft\"></div>\n\n<div style=\"background: white; border-top: 1px solid #ccc; padding: 20px 0px; font-size: 12px; color: #777;\">\n    <footer>\n        <div class=\"container\">\n            <div class=\"row\">\n                <div class=\"span12\">cichlids.com</div>\n            </div>\n            <div class=\"row\">\n                <div class=\"span8\">\n                    <div class=\"footernav\">\n                        <span style=\"font-weight: bold; padding: 0px 5px;\">About</span> |\n                        <span style=\"font-weight: bold; padding: 0px 5px;\">Imprint</span> |\n                        <span style=\"font-weight: bold; padding: 0px 5px;\">FAQ</span> |\n                        <span style=\"font-weight: bold; padding: 0px 5px;\">Contact</span> |\n                        <span style=\"font-weight: bold; padding: 0px 5px;\">Advertise</span> |\n                        <span style=\"font-weight: bold; padding: 0px 5px;\">Terms</span> |\n                        <span style=\"font-weight: bold; padding: 0px 5px;\">Privacy</span> |\n                        <span style=\"font-weight: bold; padding: 0px 5px;\">API</span>\n                    </div>\n                    <div class=\"copyright\">\n                        Copyright &copy; 1997-2016 cichlids.com.\n                    </div>\n                </div>\n            </div>\n        </div>\n    </footer>\n</div>\n"

/***/ },
/* 14 */
/***/ function(module, exports) {

	module.exports = "<div class=\"comments card\" *ngIf=\"isLoading\">\n    <spinner></spinner>\n    Loading comments...\n</div>\n<div class=\"comments card\" *ngIf=\"errorMessage\">\n    <error-message [message]=\"errorMessage\"\n        linkMessage=\"Click here to reload.\"\n        (linkClick)=\"reloadComments()\"></error-message>\n</div>\n<div class=\"comments card\" *ngIf=\"hasComments\">\n    <comment *ngFor=\"#comment of comments\" [comment]=\"comment\"></comment>\n</div>\n"

/***/ },
/* 15 */
/***/ function(module, exports) {

	module.exports = "<div class=\"media comment profile profile-comment\" *ngIf=\"comment\">\n    <a class=\"avatar pull-left\" [userProfileLink]=\"comment.poster\" *ngIf=\"false\">\n        <img class=\"media-object\" src=\"http://placehold.it/50x50\" width=\"50\" height=\"50\">\n    </a>\n    <div class=\"media-body\">\n        <div class=\"stars pull-right\" *ngIf=\"comment.rating\">\n            <img src=\"/static/img/smile{{comment.rating}}.gif\">\n        </div>\n        <div class=\"name\">\n            <a [userProfileLink]=\"comment.poster\" class=\"fullname\">{{comment.poster.name}}</a>\n            <!-- <a [userProfileLink]=\"comment.poster\" class=\"username\">@{{comment.poster.username}}</a> -->\n        </div>\n        <div class=\"body\">{{comment.body}}</div>\n        <div class=\"tstamp\">{{comment.tstamp | amFromUnix | amTimeAgo}}</div>\n    </div>\n    <div class=\"clearfix\"></div>\n</div>\n"

/***/ },
/* 16 */
/***/ function(module, exports) {

	module.exports = "                    id zufaellig enerieren und dann unten die ids prefixen: rating-zufaell-...\n\n\nfuer das mit dem avater muesste ich mal etwas basteln.\n\nman kann doch auch template vorgeben mit content.\n\n\n\n<form class=\"postcommentform\">\n\n    <div class=\"postcomment profile media\">\n        <a class=\"avatar pull-left\" href=\"/user/alexanderlanger\">\n            <img class=\"media-object\" src=\"http://placehold.it/50x50\" width=\"50\" height=\"50\">\n        </a>\n        <div class=\"media-body\">\n                bis hier ist immer gleich. nur oben sind noch ein paar classes mehr.\n\n\n            <div class=\"row\">\n                <textarea class=\"col-xs-12\" style=\"height: 100px;\" placeholder=\"Add your comment...\"></textarea>\n            </div>\n            <div class=\"row\">\n                <div class=\"col-xs-12 col-md-2\">\n                    Add rating:\n                </div>\n                <div class=\"col-xs-12 col-md-10\">\n                    <div class=\"rating\">\n                        <!-- <input type=\"radio\" name=\"rating\" value=\"5\" /> -->\n                        <!-- <label title=\"Excellent\"> -->\n                            <!-- <img src=\"static/img/smile5.gif\"> Excellent -->\n                        <!-- </label> -->\n\n                        <!-- <input type=\"radio\" name=\"rating\" value=\"4\" /> -->\n                        <!-- <label for=\"{{ratingId}}-star4\" title=\"Pretty good\"> -->\n                            <!-- <img src=\"static/img/smile4.gif\"> Pretty Good -->\n                        <!-- </label> -->\n\n                        <!-- <input type=\"radio\" name=\"rating\" value=\"3\" /> -->\n                        <!-- <label for=\"{{ratingId}}-star3\" title=\"Moderate\"> -->\n                            <!-- <img src=\"static/img/smile3.gif\"> Moderate -->\n                        <!-- </label> -->\n\n                        <!-- <input type=\"radio\" name=\"rating\" value=\"2\" /> -->\n                        <!-- <label for=\"{{ratingId}}-star2\" title=\"Poor\"> -->\n                            <!-- <img src=\"static/img/smile2.gif\"> Poor -->\n                        <!-- </label> -->\n\n                        <!-- <input type=\"radio\" name=\"rating\" value=\"1\" /> -->\n                        <!-- <label for=\"{{ratingId}}-star1\" title=\"Bad\"> -->\n                            <!-- <img src=\"static/img/smile1.gif\"> Bad -->\n                        <!-- </label> -->\n                    </div>\n                    <div class=\"clearfix\"></div>\n                </div>\n            </div>\n            <div class=\"clearfix\">\n                <button class=\"btn btn-primary pull-left\">Post Comment</button>\n                <!-- <button class=\"btn btn-small pull-right\">\n                    <span>Subscribe to comments</span>\n                    <span style=\"display: none;\" id=\"subscribe-ajaxindicator\">\n                                <img src=\"static/img/indicator.gif\">\n                            </span>\n                </button> -->\n            </div>\n            <div class=\"postcomment-feedback\"></div>\n        </div>\n    </div>\n</form>\n"

/***/ },
/* 17 */
/***/ function(module, exports) {

	module.exports = "<div class=\"row\">\n    <div class=\"col-xs-12 col-md-8\">\n        <h2>Latest Pictures</h2>\n        <picture-list [pictures]=\"pictures\"></picture-list>\n        <a [routerLink]=\"['Pictures']\">More pictures...</a>\n        <h2>Latest tank examples</h2>\n    </div>\n    <div class=\"col-xs-12 col-md-4\">\n    </div>\n</div>\n"

/***/ },
/* 18 */
/***/ function(module, exports) {

	module.exports = "<a [routerLink]=\"['/Pictures', 'PictureDetails', { slug: picture.slug }]\"\n    class=\"col-xs-12 col-sm-6 col-md-4 gallery-item\">\n    <div class=\"card\">\n        <div class=\"gallery-image-wrapper\">\n            <picture-img [picture]=\"picture\"></picture-img>\n        </div>\n        <div class=\"gallery-details\">\n            <div class=\"gallery-caption\">{{picture?.title}}</div>\n            <div class=\"profile profile-small\">{{picture?.user.name}}</div>\n            <div class=\"clearfix\"></div>\n        </div>\n    </div>\n</a>\n"

/***/ },
/* 19 */
/***/ function(module, exports) {

	module.exports = "<div class=\"row gallery\">\n    <picture-list-entry *ngFor=\"#picture of pictures\" [picture]=\"picture\"></picture-list-entry>\n</div>\n"

/***/ },
/* 20 */
/***/ function(module, exports) {

	module.exports = "<loading-screen *ngIf=\"hasLoadingScreen\"></loading-screen>\n\n<div *ngIf=\"errorMessage\">\n    <error-message [message]=\"errorMessage\"\n        linkMessage=\"Click here to reload.\"\n        (linkClick)=\"reload()\"></error-message>\n</div>\n\n<div *ngIf=\"picture\">\n\n<div class=\"row\">\n    <div class=\"col-xs-12\">\n        <h1 class=\"single-picture-title\">{{picture?.title}}</h1>\n    </div>\n\n<!-- <a id=\"prevImage\" href=\"#\" wicket:id=\"prevImage\"><i class=\"icon-chevron-left icon-2x\"></i></a> -->\n<!-- <a id=\"nextImage\" href=\"#\" wicket:id=\"nextImage\"><i class=\"icon-chevron-right icon-2x\"></i></a> -->\n\n<div class=\"row\">\n    <div class=\"col-xs-12 col-md-8\">\n        <picture-img [picture]=\"picture\" styleClass=\"single-picture-image\"></picture-img>\n\n        <div class=\"row\">\n            <div class=\"single-picture-share\">\n                <!-- <wicket:enclosure child=\"favorite\"> -->\n                    <!-- <div class=\"pull-left\"> -->\n                        <!-- <button class=\"btn btn-small\" wicket:id=\"favorite\"> -->\n                            <!-- <span wicket:id=\"buttonText\"><i class=\"icon-heart icon-white\"></i> Favorite</span> -->\n                            <!-- <span style=\"display: none;\" id=\"favorite-ajaxindicator\"> -->\n                                    <!-- <img src=\"static/img/indicator.gif\"> -->\n                                <!-- </span> -->\n                        <!-- </button> -->\n\n                        <!-- <a href=\"#collectModal\" class=\"btn btn-info btn-small\" role=\"button\" data-toggle=\"modal\"><i class=\"icon-plus\"></i> Collect</a> -->\n\n                        <!-- <form wicket:id=\"collectForm\" id=\"collectModal\" class=\"modal hide fade\" tabindex=\"-1\" role=\"dialog\" aria-labelledby=\"myModalLabel\" aria-hidden=\"true\"> -->\n                            <!-- <div class=\"modal-header\"> -->\n                                <!-- <button type=\"button\" class=\"close\" data-dismiss=\"modal\" aria-hidden=\"true\"> -->\n                                    <!-- <i class=\"icon-remove-circle\"></i> -->\n                                <!-- </button> -->\n                                <!-- <h3 id=\"myModalLabel\">Add to Gallery</h3> -->\n                            <!-- </div> -->\n                            <!-- <div class=\"modal-body\"> -->\n                                <!-- <fieldset> -->\n                                    <!-- <div class=\"control-group\"> -->\n                                        <!-- <label class=\"control-label\" for=\"collectGallery\"> -->\n                                            <!-- Select a gallery where you want to add this picture: -->\n                                        <!-- </label> -->\n                                        <!-- <div class=\"controls\"> -->\n                                            <!-- <select id=\"collectGallery\" wicket:id=\"collectGallery\"></select> -->\n                                        <!-- </div> -->\n                                    <!-- </div> -->\n                                <!-- </fieldset> -->\n                                <!-- <div wicket:id=\"feedback\"></div> -->\n                            <!-- </div> -->\n                            <!-- <div class=\"modal-footer\"> -->\n                                <!-- <button class=\"btn\" data-dismiss=\"modal\" aria-hidden=\"true\">Close</button> -->\n                                <!-- <button type=\"submit\" class=\"btn btn-primary\" wicket:id=\"submit\"> -->\n                                    <!-- <i class=\"icon-plus\"></i> Add -->\n                                <!-- </button> -->\n                            <!-- </div> -->\n                        <!-- </form> -->\n                    <!-- </div> -->\n                <!-- </wicket:enclosure> -->\n                <!-- <div class=\"pull-right\"> -->\n                    <!-- <a rel=\"nofollow\" href=\"http://www.facebook.com/share.php?u={{myurl}}\" class=\"btn btn-small\" (click)=\"shareFb()\" -->\n                    <!-- target=\"_blank\"><img src=\"static/img/f_logo.png\" width=\"14\" height=\"14\" border=\"0\" /></a> -->\n<!--  -->\n                    <!-- <a rel=\"nofollow\" href=\"#\" class=\"btn btn-small\"><img src=\"static/img/twitter.png\" width=\"14\" height=\"14\" border=\"0\"> -->\n                    <!-- </a> -->\n<!--  -->\n                    <!-- <a rel=\"nofollow\" href=\"#\" class=\"btn btn-small\"><img src=\"static/img/google_logo.png\" width=\"14\" height=\"14\" border=\"0\"> -->\n                    <!-- </a> -->\n                    <!-- <a rel=\"nofollow\" href=\"//pinterest.com/pin/create/button/?url=http%3A%2F%2Fwww.flickr.com%2Fphotos%2Fkentbrew%2F6851755809%2F&media=http%3A%2F%2Ffarm8.staticflickr.com%2F7027%2F6851755809_df5b2051c9_z.jpg&description=Next%20stop%3A%20Pinterest\" class=\"btn btn-small\"><img src=\"static/img/pinterest.png\" width=\"14\" height=\"14\" border=\"0\"> -->\n                    <!-- </a> -->\n                    <!-- <a rel=\"nofollow\" href=\"#\" class=\"btn btn-small\"><img src=\"static/img/stumpleupon.png\" width=\"14\" height=\"14\" border=\"0\"> -->\n                    <!-- </a> -->\n\n                    <!-- <a href=\"#\" class=\"btn btn-small\"><i class=\"icon-envelope\"></i></a> -->\n                    <!-- <a href=\"#\" class=\"btn btn-small\"><i class=\"icon-share\"></i></a> -->\n                    <!-- Popup: share this photo by mail, siehe 500px -->\n                <!-- </div> -->\n                <div class=\"clearfix\"></div>\n            </div>\n        </div>\n        <div [class.hidden]=\"!picture?.description\" class=\"well well-small single-picture-descr\">\n            {{picture?.description}}\n        </div>\n        <!-- <post-comment></post-comment> -->\n        <comment-list [url]=\"commentsUrl\"></comment-list>\n    </div>\n    <div class=\"col-xs-12 col-md-4\">\n        <user-profile-card [user]=\"picture?.user\"></user-profile-card>\n\n        <div class=\"card\">\n            Hier Moderations Interface\n        </div>\n\n        <div class=\"card card-pictureinfo\">\n            <div><i class=\"fa fa-upload\"></i> posted\n                <time>{{picture?.tstamp | amFromUnix | amTimeAgo}}</time></div>\n            <!-- <div><i class=\"fa fa-eye-open\"></i> <b wicket:id=\"numViews\">1234</b> views</div> -->\n            <!-- <div><i class=\"fa fa-star\"></i> rated {{picture?.rating}}</div> -->\n            <!-- <div><i class=\"fa fa-heart\"></i> <b wicket:id=\"numFavorites\">1234</b> favorites</div> -->\n            <!-- <div><i class=\"fa fa-picture\"></i> Cichlids</div> -->\n            <!-- <div><i class=\"fa fa-list\"></i> Tanganyika</div> -->\n        </div>\n\n        <!--div class=\"card card-morepics\">\n                <div class=\"title\">More from\n                    <b class=\"username\"><span wicket:id=\"poster.name\">Alexander Langer</span></b>\n                </div>\n                <div class=\"row-fluid\">\n                    <ul class=\"thumbnails\">\n                        <li class=\"span4\">\n                            <div class=\"thumbnail\">\n                                <a href=\"single.html\"><img src=\"http://placehold.it/100x100.jpg\" alt=\"\"></a>\n                            </div>\n                        </li>\n                        <li class=\"span4\">\n                            <div class=\"thumbnail\">\n                                <a href=\"single.html\"><img src=\"http://placehold.it/100x100.jpg\" alt=\"\"></a>\n                            </div>\n                        </li>\n                        <li class=\"span4\">\n                            <div class=\"thumbnail\">\n                                <a href=\"single.html\"><img src=\"http://placehold.it/100x100.jpg\" alt=\"\"></a>\n                            </div>\n                        </li>\n                    </ul>\n                </div>\n            </div-->\n\n        <!-- <wicket:enclosure> -->\n            <!-- <div class=\"card card-tags\"> -->\n                <!-- <div class=\"title\"><i class=\"icon-tags\"></i> Tags</div> -->\n                <!-- <div wicket:id=\"tags\"></div> -->\n            <!-- </div> -->\n        <!-- </wicket:enclosure> -->\n\n        <!-- <div class=\"card card-embed hidden-phone\"> -->\n            <!-- <div class=\"title\"><i class=\"icon-share\"></i> -->\n                <!-- <a href=\"#embed-content\" class=\"embedlink\">Embed</a> -->\n                <!-- <div id=\"embed-content\" style=\"display: none;\"> -->\n                    <!-- <h3>Embed Picture</h3> -->\n<!--  -->\n                    <!-- <div class=\"accordion embed-accordion\"> -->\n<!--  -->\n                        <!-- <div class=\"accordion-group\"> -->\n                            <!-- <div class=\"accordion-heading\"> -->\n                                <!-- <a class=\"accordion-toggle\" data-toggle=\"collapse\" data-parent=\".embed-accordion\" href=\"#collapseLink\"> -->\n                                    <!-- <i class=\"embed-icon icon-chevron-down\"></i> Link </a> -->\n                            <!-- </div> -->\n                            <!-- <div id=\"collapseLink\" class=\"accordion-body collapse in\"> -->\n                                <!-- <div class=\"accordion-inner\"> -->\n                                    <!-- <p><strong>This a link to this picture. Just copy and paste!</strong></p> -->\n                                    <!-- <input type=\"text\" onclick=\"this.focus(); -->\n                                <!-- this.select()\" readonly=\"readonly\" id=\"embed-link-body\" wicket:id=\"embed-link\" value=\"http://www.cichlids.com/foo/bar\" class=\"input-xxlarge\" /> -->\n                                <!-- </div> -->\n                            <!-- </div> -->\n                        <!-- </div> -->\n<!--  -->\n                        <!-- <div class=\"accordion-group\"> -->\n                            <!-- <div class=\"accordion-heading\"> -->\n                                <!-- <a class=\"accordion-toggle\" data-toggle=\"collapse\" data-parent=\".embed-accordion\" href=\"#collapseBBCode\"> -->\n                                    <!-- <i class=\"embed-icon icon-chevron-right\"></i> BBCode (Forums) -->\n                                <!-- </a> -->\n                            <!-- </div> -->\n                            <!-- <div id=\"collapseBBCode\" class=\"accordion-body collapse\"> -->\n                                <!-- <div class=\"accordion-inner\"> -->\n                                    <!-- <div class=\"embed-preview pull-left\"> -->\n                                        <!-- <a href=\"#\" wicket:id=\"preview-bbcode-linkimg\"><img src=\"http://placehold.it/200x200.jpg\" width=\"200\" height=\"200\" wicket:id=\"preview-bbcode-img\"></a> -->\n                                        <!-- <p> -->\n                                            <!-- <a href=\"#\" wicket:id=\"preview-bbcode-linktitle\"> -->\n                                                <!-- <span wicket:id=\"title\"></span></a> -->\n                                            <!-- by -->\n                                            <!-- <a href=\"#\" wicket:id=\"preview-bbcode-linkuser\"> -->\n                                                <!-- <span wicket:id=\"poster.name\"></span></a> -->\n                                        <!-- </p> -->\n                                    <!-- </div> -->\n<!--  -->\n                                    <!-- <div class=\"embed-copy\"> -->\n                                        <!-- <p><strong>Copy the code to a forum</strong></p> -->\n                                        <!-- <textarea onclick=\"this.focus(); -->\n                                <!-- this.select()\" readonly=\"readonly\" wrap=\"virtual\" wicket:id=\"embed-bbcode\"></textarea> -->\n                                    <!-- </div> -->\n<!--  -->\n                                <!-- </div> -->\n                            <!-- </div> -->\n                        <!-- </div> -->\n<!--  -->\n                        <!-- <div class=\"accordion-group\"> -->\n                            <!-- <div class=\"accordion-heading\"> -->\n                                <!-- <a class=\"accordion-toggle\" data-toggle=\"collapse\" data-parent=\".embed-accordion\" href=\"#collapseHTML\"> -->\n                                    <!-- <i class=\"embed-icon icon-chevron-right\"></i> HTML -->\n                                <!-- </a> -->\n                            <!-- </div> -->\n                            <!-- <div id=\"collapseHTML\" class=\"accordion-body collapse\"> -->\n                                <!-- <div class=\"accordion-inner\"> -->\n                                    <!-- <div class=\"embed-preview pull-left\"> -->\n                                        <!-- <a href=\"#\" wicket:id=\"preview-html-linkimg\"><img src=\"http://placehold.it/200x200.jpg\" width=\"200\" height=\"200\" wicket:id=\"preview-html-img\"></a> -->\n                                        <!-- <p> -->\n                                            <!-- <strong><a href=\"#\" wicket:id=\"preview-html-linktitle\"> -->\n                                                        <!-- <span wicket:id=\"title\"></span></a></strong> -->\n                                            <!-- <br/> -->\n                                            <!-- <small> -->\n                                                    <!-- by <a href=\"#\" -->\n                                                          <!-- wicket:id=\"preview-html-linkuser\"> -->\n                                                        <!-- <span wicket:id=\"poster.name\"></span></a> -->\n                                                <!-- </small> -->\n                                        <!-- </p> -->\n                                    <!-- </div> -->\n                                    <!-- <div class=\"embed-copy\"> -->\n                                        <!-- <p><strong>Copy the code to your website</strong></p> -->\n                                        <!-- <textarea onclick=\"this.focus(); -->\n                                <!-- this.select()\" readonly=\"readonly\" wrap=\"virtual\" wicket:id=\"embed-html\"></textarea> -->\n                                    <!-- </div> -->\n                                <!-- </div> -->\n                            <!-- </div> -->\n                        <!-- </div> -->\n                    <!-- </div> -->\n                <!-- </div> -->\n            <!-- </div> -->\n        <!-- </div> -->\n<!--  -->\n        <!-- <div class=\"pull-right\"> -->\n            <!-- <a href=\"#\" wicket:id=\"report\" class=\"report-pic\">Report -->\n                    <!-- <span style=\"display: none;\" id=\"report-ajaxindicator\"> -->\n                        <!-- <img src=\"static/img/indicator.gif\"> -->\n                    <!-- </span> -->\n                <!-- </a> -->\n        <!-- </div> -->\n\n    </div>\n</div>\n\n</div>\n"

/***/ },
/* 21 */
/***/ function(module, exports) {

	module.exports = "<div class=\"text-center\">\n<pagination\n    [(ngModel)]=\"currentPage\"\n    [directionLinks]=\"true\"\n    [boundaryLinks]=\"false\"\n    previousText=\"&lsaquo;\" nextText=\"&rsaquo;\" firstText=\"&laquo;\" lastText=\"&raquo;\"\n    [maxSize]=\"5\"\n    [totalItems]=\"totalCount\"\n    (pageChanged)=\"pageChanged($event)\"></pagination>\n</div>\n\n<ul class=\"nav nav-pills\">\n    <!-- Popular -->\n    <!-- Trending -->\n    <li [class.active]=\"isSort('newest')\"><a [routerLink]=\"['PictureList', {sort: 'newest'}]\">Newest</a></li>\n    <li [class.active]=\"isSort('views')\"><a [routerLink]=\"['PictureList', {sort: 'views'}]\">Most viewed</a></li>\n</ul>\n\n<hr>\n\n<a name=\"pics\">\n<picture-list [pictures]=\"pictures\"></picture-list>\n\n<div class=\"text-center\">\n<pagination\n    [(ngModel)]=\"currentPage\"\n    [directionLinks]=\"true\"\n    [boundaryLinks]=\"false\"\n    previousText=\"&lsaquo;\" nextText=\"&rsaquo;\" firstText=\"&laquo;\" lastText=\"&raquo;\"\n    [maxSize]=\"5\"\n    [totalItems]=\"totalCount\"\n    (pageChanged)=\"pageChanged($event); \"></pagination>\n</div>\n"

/***/ },
/* 22 */
/***/ function(module, exports, __webpack_require__) {

	"use strict";
	var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
	    var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
	    if (typeof Reflect === "object" && typeof Reflect.decorate === "function") r = Reflect.decorate(decorators, target, key, desc);
	    else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
	    return c > 3 && r && Object.defineProperty(target, key, r), r;
	};
	var __metadata = (this && this.__metadata) || function (k, v) {
	    if (typeof Reflect === "object" && typeof Reflect.metadata === "function") return Reflect.metadata(k, v);
	};
	var core_1 = __webpack_require__(1);
	var router_1 = __webpack_require__(2);
	var view_pictures_component_1 = __webpack_require__(31);
	var view_frontpage_component_1 = __webpack_require__(27);
	var picture_service_1 = __webpack_require__(3);
	var comment_service_1 = __webpack_require__(7);
	var picture_config_service_1 = __webpack_require__(4);
	var AppComponent = (function () {
	    function AppComponent() {
	    }
	    AppComponent = __decorate([
	        core_1.Component({
	            selector: 'cichlids-app',
	            template: __webpack_require__(13),
	            encapsulation: core_1.ViewEncapsulation.None,
	            directives: [
	                router_1.ROUTER_DIRECTIVES,
	            ],
	            providers: [
	                // HeaderService,
	                picture_service_1.PictureService,
	                picture_config_service_1.PictureConfigService,
	                comment_service_1.CommentService,
	            ],
	        }),
	        router_1.RouteConfig([
	            {
	                name: 'Frontpage',
	                path: '/',
	                component: view_frontpage_component_1.ViewFrontpageComponent,
	                useAsDefault: true
	            },
	            {
	                name: 'Pictures',
	                path: '/pictures/...',
	                component: view_pictures_component_1.ViewPicturesComponent,
	            },
	        ]), 
	        __metadata('design:paramtypes', [])
	    ], AppComponent);
	    return AppComponent;
	}());
	exports.AppComponent = AppComponent;


/***/ },
/* 23 */
/***/ function(module, exports, __webpack_require__) {

	"use strict";
	var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
	    var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
	    if (typeof Reflect === "object" && typeof Reflect.decorate === "function") r = Reflect.decorate(decorators, target, key, desc);
	    else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
	    return c > 3 && r && Object.defineProperty(target, key, r), r;
	};
	var __metadata = (this && this.__metadata) || function (k, v) {
	    if (typeof Reflect === "object" && typeof Reflect.metadata === "function") return Reflect.metadata(k, v);
	};
	var core_1 = __webpack_require__(1);
	var comment_service_1 = __webpack_require__(7);
	var comment_component_1 = __webpack_require__(24);
	var CommentListComponent = (function () {
	    function CommentListComponent(_commentService) {
	        this._commentService = _commentService;
	        this.comments = [];
	        this.isLoading = false;
	        this.hasComments = false;
	    }
	    CommentListComponent.prototype.ngOnInit = function () {
	        this.reloadComments();
	    };
	    CommentListComponent.prototype.reloadComments = function () {
	        var _this = this;
	        this.errorMessage = null;
	        this.hasComments = false;
	        if (this.url) {
	            this.isLoading = true;
	            this._commentService.listCommentsFromUrl(this.url).subscribe(function (list) { return _this.setComments(list); }, function (error) { return _this.setError(error); });
	        }
	    };
	    CommentListComponent.prototype.setComments = function (list) {
	        this.isLoading = false;
	        this.errorMessage = null;
	        if (list && list.length > 0)
	            this.hasComments = true;
	        this.comments = list;
	    };
	    CommentListComponent.prototype.setError = function (e) {
	        this.isLoading = false;
	        console.error("Error loading comments", e);
	        this.errorMessage = "Error loading comments from server.";
	        this.comments = [];
	    };
	    __decorate([
	        core_1.Input(), 
	        __metadata('design:type', Array)
	    ], CommentListComponent.prototype, "comments", void 0);
	    __decorate([
	        core_1.Input(), 
	        __metadata('design:type', String)
	    ], CommentListComponent.prototype, "url", void 0);
	    CommentListComponent = __decorate([
	        core_1.Component({
	            selector: 'comment-list',
	            template: __webpack_require__(14),
	            directives: [
	                comment_component_1.CommentComponent,
	            ],
	        }), 
	        __metadata('design:paramtypes', [comment_service_1.CommentService])
	    ], CommentListComponent);
	    return CommentListComponent;
	}());
	exports.CommentListComponent = CommentListComponent;


/***/ },
/* 24 */
/***/ function(module, exports, __webpack_require__) {

	"use strict";
	var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
	    var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
	    if (typeof Reflect === "object" && typeof Reflect.decorate === "function") r = Reflect.decorate(decorators, target, key, desc);
	    else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
	    return c > 3 && r && Object.defineProperty(target, key, r), r;
	};
	var __metadata = (this && this.__metadata) || function (k, v) {
	    if (typeof Reflect === "object" && typeof Reflect.metadata === "function") return Reflect.metadata(k, v);
	};
	var core_1 = __webpack_require__(1);
	var router_1 = __webpack_require__(2);
	var user_profile_link_directive_1 = __webpack_require__(5);
	var CommentComponent = (function () {
	    function CommentComponent() {
	    }
	    __decorate([
	        core_1.Input(), 
	        __metadata('design:type', Array)
	    ], CommentComponent.prototype, "comment", void 0);
	    CommentComponent = __decorate([
	        core_1.Component({
	            selector: 'comment',
	            template: __webpack_require__(15),
	            directives: [
	                router_1.ROUTER_DIRECTIVES,
	                user_profile_link_directive_1.UserProfileLink,
	            ],
	        }), 
	        __metadata('design:paramtypes', [])
	    ], CommentComponent);
	    return CommentComponent;
	}());
	exports.CommentComponent = CommentComponent;


/***/ },
/* 25 */
/***/ function(module, exports, __webpack_require__) {

	"use strict";
	var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
	    var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
	    if (typeof Reflect === "object" && typeof Reflect.decorate === "function") r = Reflect.decorate(decorators, target, key, desc);
	    else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
	    return c > 3 && r && Object.defineProperty(target, key, r), r;
	};
	var __metadata = (this && this.__metadata) || function (k, v) {
	    if (typeof Reflect === "object" && typeof Reflect.metadata === "function") return Reflect.metadata(k, v);
	};
	var core_1 = __webpack_require__(1);
	var PostCommentComponent = (function () {
	    function PostCommentComponent() {
	        this.ratingId = "" + Math.floor(Math.random() * 1000000);
	    }
	    PostCommentComponent = __decorate([
	        core_1.Component({
	            selector: 'post-comment',
	            template: __webpack_require__(16),
	        }), 
	        __metadata('design:paramtypes', [])
	    ], PostCommentComponent);
	    return PostCommentComponent;
	}());
	exports.PostCommentComponent = PostCommentComponent;


/***/ },
/* 26 */
/***/ function(module, exports, __webpack_require__) {

	"use strict";
	var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
	    var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
	    if (typeof Reflect === "object" && typeof Reflect.decorate === "function") r = Reflect.decorate(decorators, target, key, desc);
	    else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
	    return c > 3 && r && Object.defineProperty(target, key, r), r;
	};
	var __metadata = (this && this.__metadata) || function (k, v) {
	    if (typeof Reflect === "object" && typeof Reflect.metadata === "function") return Reflect.metadata(k, v);
	};
	var __param = (this && this.__param) || function (paramIndex, decorator) {
	    return function (target, key) { decorator(target, key, paramIndex); }
	};
	var core_1 = __webpack_require__(1);
	var common_1 = __webpack_require__(36);
	var paginationConfig = {
	    maxSize: void 0,
	    itemsPerPage: 10,
	    boundaryLinks: false,
	    directionLinks: true,
	    firstText: 'First',
	    previousText: 'Previous',
	    nextText: 'Next',
	    lastText: 'Last',
	    rotate: true
	};
	var PAGINATION_TEMPLATE = "\n  <ul class=\"pagination\" [ngClass]=\"classMap\">\n    <li class=\"pagination-first page-item\"\n        *ngIf=\"boundaryLinks\"\n        [class.disabled]=\"noPrevious()||disabled\">\n      <a class=\"page-link\" href (click)=\"selectPage(1, $event)\" [innerHTML]=\"getText('first')\"></a>\n    </li>\n\n    <li class=\"pagination-prev page-item\"\n        *ngIf=\"directionLinks\"\n        [class.disabled]=\"noPrevious()||disabled\">\n      <a class=\"page-link\" href (click)=\"selectPage(page - 1, $event)\" [innerHTML]=\"getText('previous')\"></a>\n      </li>\n\n    <li *ngFor=\"#pg of pages\"\n        [class.active]=\"pg.active\"\n        [class.disabled]=\"disabled&&!pg.active\"\n        class=\"pagination-page page-item\">\n      <a class=\"page-link\" href (click)=\"selectPage(pg.number, $event)\" [innerHTML]=\"pg.text\"></a>\n    </li>\n\n    <li class=\"pagination-next page-item\"\n        *ngIf=\"directionLinks\"\n        [class.disabled]=\"noNext()\">\n      <a class=\"page-link\" href (click)=\"selectPage(page + 1, $event)\" [innerHTML]=\"getText('next')\"></a></li>\n\n    <li class=\"pagination-last page-item\"\n        *ngIf=\"boundaryLinks\"\n        [class.disabled]=\"noNext()\">\n      <a class=\"page-link\" href (click)=\"selectPage(totalPages, $event)\" [innerHTML]=\"getText('last')\"></a></li>\n  </ul>\n  ";
	var Pagination = (function () {
	    function Pagination(cd, renderer, elementRef) {
	        this.cd = cd;
	        this.renderer = renderer;
	        this.elementRef = elementRef;
	        this.numPages = new core_1.EventEmitter();
	        this.pageChanged = new core_1.EventEmitter();
	        this.inited = false;
	        this.onChange = function (_) {
	        };
	        this.onTouched = function () {
	        };
	        cd.valueAccessor = this;
	        this.config = this.config || paginationConfig;
	    }
	    Object.defineProperty(Pagination.prototype, "itemsPerPage", {
	        get: function () {
	            return this._itemsPerPage;
	        },
	        set: function (v) {
	            this._itemsPerPage = v;
	            this.totalPages = this.calculateTotalPages();
	        },
	        enumerable: true,
	        configurable: true
	    });
	    Object.defineProperty(Pagination.prototype, "totalItems", {
	        get: function () {
	            return this._totalItems;
	        },
	        set: function (v) {
	            this._totalItems = v;
	            this.totalPages = this.calculateTotalPages();
	        },
	        enumerable: true,
	        configurable: true
	    });
	    Object.defineProperty(Pagination.prototype, "page", {
	        get: function () {
	            return this._page;
	        },
	        set: function (value) {
	            var _previous = this._page;
	            if (this.totalPages === undefined) {
	                this._page = value || 0;
	                return;
	            }
	            else {
	                this._page = (value > this.totalPages) ? this.totalPages : (value || 1);
	            }
	            // console.log("setting page value to ", value, "previous", _previous,
	            // "pages", this.totalPages, "this.page =", this._page, _previous === this._page);
	            if (_previous === this._page || typeof _previous === 'undefined') {
	                return;
	            }
	            this.pageChanged.emit({
	                page: this._page,
	                itemsPerPage: this.itemsPerPage
	            });
	        },
	        enumerable: true,
	        configurable: true
	    });
	    Pagination.prototype.ngOnInit = function () {
	        this.classMap = this.elementRef.nativeElement.getAttribute('class') || '';
	        // watch for maxSize
	        this.maxSize = typeof this.maxSize !== 'undefined' ? this.maxSize : paginationConfig.maxSize;
	        this.rotate = typeof this.rotate !== 'undefined' ? this.rotate : paginationConfig.rotate;
	        this.boundaryLinks = typeof this.boundaryLinks !== 'undefined' ? this.boundaryLinks : paginationConfig.boundaryLinks;
	        this.directionLinks = typeof this.directionLinks !== 'undefined' ? this.directionLinks : paginationConfig.directionLinks;
	        // base class
	        this.itemsPerPage = typeof this.itemsPerPage !== 'undefined' ? this.itemsPerPage : paginationConfig.itemsPerPage;
	        this.totalPages = this.calculateTotalPages();
	        // this class
	        this.pages = this.getPages(this.page, this.totalPages);
	        this.page = this.cd.value;
	        this.inited = true;
	    };
	    Pagination.prototype.writeValue = function (value) {
	        this.page = value;
	        this.pages = this.getPages(this.page, this.totalPages);
	    };
	    Pagination.prototype.registerOnChange = function (fn) {
	        this.onChange = fn;
	    };
	    Pagination.prototype.registerOnTouched = function (fn) {
	        this.onTouched = fn;
	    };
	    Pagination.prototype.selectPage = function (page, event) {
	        if (event) {
	            event.preventDefault();
	        }
	        if (!this.disabled) {
	            if (event && event.target) {
	                var target = event.target;
	                target.blur();
	            }
	            this.writeValue(page);
	            this.cd.viewToModelUpdate(this.page);
	        }
	    };
	    Pagination.prototype.getText = function (key) {
	        return this[key + 'Text'] || paginationConfig[key + 'Text'];
	    };
	    Pagination.prototype.noPrevious = function () {
	        return this.page === 1;
	    };
	    Pagination.prototype.noNext = function () {
	        return this.page === this.totalPages;
	    };
	    // Create page object used in template
	    Pagination.prototype.makePage = function (number, text, isActive) {
	        return {
	            number: number,
	            text: text,
	            active: isActive
	        };
	    };
	    Pagination.prototype.getPages = function (currentPage, totalPages) {
	        var pages = [];
	        // Default page limits
	        var startPage = 1;
	        var endPage = totalPages;
	        var isMaxSized = typeof this.maxSize !== 'undefined' && this.maxSize < totalPages;
	        // recompute if maxSize
	        if (isMaxSized) {
	            if (this.rotate) {
	                // Current page is displayed in the middle of the visible ones
	                startPage = Math.max(currentPage - Math.floor(this.maxSize / 2), 1);
	                endPage = startPage + this.maxSize - 1;
	                // Adjust if limit is exceeded
	                if (endPage > totalPages) {
	                    endPage = totalPages;
	                    startPage = endPage - this.maxSize + 1;
	                }
	            }
	            else {
	                // Visible pages are paginated with maxSize
	                startPage = ((Math.ceil(currentPage / this.maxSize) - 1) * this.maxSize) + 1;
	                // Adjust last page if limit is exceeded
	                endPage = Math.min(startPage + this.maxSize - 1, totalPages);
	            }
	        }
	        // Add page number links
	        for (var number = startPage; number <= endPage; number++) {
	            var page = this.makePage(number, number.toString(), number === currentPage);
	            pages.push(page);
	        }
	        // Add links to move between page sets
	        if (isMaxSized && !this.rotate) {
	            if (startPage > 1) {
	                var previousPageSet = this.makePage(startPage - 1, '...', false);
	                pages.unshift(previousPageSet);
	            }
	            if (endPage < totalPages) {
	                var nextPageSet = this.makePage(endPage + 1, '...', false);
	                pages.push(nextPageSet);
	            }
	        }
	        return pages;
	    };
	    // base class
	    Pagination.prototype.calculateTotalPages = function () {
	        if (this.totalItems === undefined)
	            return undefined;
	        var totalPages = this.itemsPerPage < 1 ? 1 : Math.ceil(this.totalItems / this.itemsPerPage);
	        return Math.max(totalPages || 0, 1);
	    };
	    Object.defineProperty(Pagination.prototype, "totalPages", {
	        get: function () {
	            return this._totalPages;
	        },
	        set: function (v) {
	            this._totalPages = v;
	            this.numPages.emit(v);
	            if (this.inited) {
	                this.selectPage(this.page);
	            }
	        },
	        enumerable: true,
	        configurable: true
	    });
	    __decorate([
	        core_1.Input(), 
	        __metadata('design:type', Number)
	    ], Pagination.prototype, "maxSize", void 0);
	    __decorate([
	        core_1.Input(), 
	        __metadata('design:type', Boolean)
	    ], Pagination.prototype, "boundaryLinks", void 0);
	    __decorate([
	        core_1.Input(), 
	        __metadata('design:type', Boolean)
	    ], Pagination.prototype, "directionLinks", void 0);
	    __decorate([
	        core_1.Input(), 
	        __metadata('design:type', String)
	    ], Pagination.prototype, "firstText", void 0);
	    __decorate([
	        core_1.Input(), 
	        __metadata('design:type', String)
	    ], Pagination.prototype, "previousText", void 0);
	    __decorate([
	        core_1.Input(), 
	        __metadata('design:type', String)
	    ], Pagination.prototype, "nextText", void 0);
	    __decorate([
	        core_1.Input(), 
	        __metadata('design:type', String)
	    ], Pagination.prototype, "lastText", void 0);
	    __decorate([
	        core_1.Input(), 
	        __metadata('design:type', Boolean)
	    ], Pagination.prototype, "rotate", void 0);
	    __decorate([
	        core_1.Input(), 
	        __metadata('design:type', Boolean)
	    ], Pagination.prototype, "disabled", void 0);
	    __decorate([
	        core_1.Output(), 
	        __metadata('design:type', core_1.EventEmitter)
	    ], Pagination.prototype, "numPages", void 0);
	    __decorate([
	        core_1.Output(), 
	        __metadata('design:type', core_1.EventEmitter)
	    ], Pagination.prototype, "pageChanged", void 0);
	    __decorate([
	        core_1.Input(), 
	        __metadata('design:type', Object)
	    ], Pagination.prototype, "itemsPerPage", null);
	    __decorate([
	        core_1.Input(), 
	        __metadata('design:type', Number)
	    ], Pagination.prototype, "totalItems", null);
	    Pagination = __decorate([
	        core_1.Component({
	            selector: 'pagination',
	            template: PAGINATION_TEMPLATE,
	            directives: [common_1.NgFor, common_1.NgIf]
	        }),
	        __param(0, core_1.Self()), 
	        __metadata('design:paramtypes', [common_1.NgModel, core_1.Renderer, core_1.ElementRef])
	    ], Pagination);
	    return Pagination;
	}());
	exports.Pagination = Pagination;


/***/ },
/* 27 */
/***/ function(module, exports, __webpack_require__) {

	"use strict";
	var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
	    var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
	    if (typeof Reflect === "object" && typeof Reflect.decorate === "function") r = Reflect.decorate(decorators, target, key, desc);
	    else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
	    return c > 3 && r && Object.defineProperty(target, key, r), r;
	};
	var __metadata = (this && this.__metadata) || function (k, v) {
	    if (typeof Reflect === "object" && typeof Reflect.metadata === "function") return Reflect.metadata(k, v);
	};
	var core_1 = __webpack_require__(1);
	var router_1 = __webpack_require__(2);
	var picture_service_1 = __webpack_require__(3);
	var picture_list_component_1 = __webpack_require__(10);
	var ViewFrontpageComponent = (function () {
	    function ViewFrontpageComponent(_pictureService) {
	        var _this = this;
	        this._pictureService = _pictureService;
	        this._pictureService.listPictures({ count: 12 }).subscribe(function (l) { return _this.pictures = l.pictures; }, function (error) { return _this.errorMessage = error; });
	    }
	    ViewFrontpageComponent = __decorate([
	        core_1.Component({
	            template: __webpack_require__(17),
	            directives: [
	                router_1.ROUTER_DIRECTIVES,
	                picture_list_component_1.PictureListComponent,
	            ],
	        }), 
	        __metadata('design:paramtypes', [picture_service_1.PictureService])
	    ], ViewFrontpageComponent);
	    return ViewFrontpageComponent;
	}());
	exports.ViewFrontpageComponent = ViewFrontpageComponent;


/***/ },
/* 28 */
/***/ function(module, exports, __webpack_require__) {

	"use strict";
	var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
	    var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
	    if (typeof Reflect === "object" && typeof Reflect.decorate === "function") r = Reflect.decorate(decorators, target, key, desc);
	    else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
	    return c > 3 && r && Object.defineProperty(target, key, r), r;
	};
	var __metadata = (this && this.__metadata) || function (k, v) {
	    if (typeof Reflect === "object" && typeof Reflect.metadata === "function") return Reflect.metadata(k, v);
	};
	var core_1 = __webpack_require__(1);
	var router_1 = __webpack_require__(2);
	var picture_img_component_1 = __webpack_require__(9);
	var user_profile_link_directive_1 = __webpack_require__(5);
	var PictureListEntryComponent = (function () {
	    function PictureListEntryComponent() {
	    }
	    __decorate([
	        core_1.Input(), 
	        __metadata('design:type', Array)
	    ], PictureListEntryComponent.prototype, "picture", void 0);
	    PictureListEntryComponent = __decorate([
	        core_1.Component({
	            selector: 'picture-list-entry',
	            template: __webpack_require__(18),
	            directives: [
	                router_1.ROUTER_DIRECTIVES,
	                picture_img_component_1.PictureImgComponent,
	                user_profile_link_directive_1.UserProfileLink,
	            ],
	        }), 
	        __metadata('design:paramtypes', [])
	    ], PictureListEntryComponent);
	    return PictureListEntryComponent;
	}());
	exports.PictureListEntryComponent = PictureListEntryComponent;


/***/ },
/* 29 */
/***/ function(module, exports, __webpack_require__) {

	"use strict";
	var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
	    var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
	    if (typeof Reflect === "object" && typeof Reflect.decorate === "function") r = Reflect.decorate(decorators, target, key, desc);
	    else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
	    return c > 3 && r && Object.defineProperty(target, key, r), r;
	};
	var __metadata = (this && this.__metadata) || function (k, v) {
	    if (typeof Reflect === "object" && typeof Reflect.metadata === "function") return Reflect.metadata(k, v);
	};
	var core_1 = __webpack_require__(1);
	var router_1 = __webpack_require__(2);
	var Config = __webpack_require__(6);
	var picture_service_1 = __webpack_require__(3);
	var picture_img_component_1 = __webpack_require__(9);
	var user_profile_card_component_1 = __webpack_require__(32);
	var comment_list_component_1 = __webpack_require__(23);
	var post_comment_component_1 = __webpack_require__(25);
	var ViewPictureDetailsComponent = (function () {
	    function ViewPictureDetailsComponent(_pictureService, _routeParams, _location) {
	        this._pictureService = _pictureService;
	        this._routeParams = _routeParams;
	        this._location = _location;
	        this.showLoadingScreen = true;
	    }
	    ViewPictureDetailsComponent.prototype.ngOnInit = function () {
	        this.reload();
	    };
	    ViewPictureDetailsComponent.prototype.reload = function () {
	        var _this = this;
	        this.showLoadingScreen = true;
	        this.errorMessage = null;
	        this.picture = null;
	        var slug = this._routeParams.get('slug');
	        this._pictureService.viewPicture(slug).subscribe(function (p) { return _this.setPicture(p); }, function (error) { return _this.setError(error); });
	    };
	    ViewPictureDetailsComponent.prototype.setPicture = function (p) {
	        this.showLoadingScreen = false;
	        this.errorMessage = null;
	        this.picture = p;
	        this.commentsUrl = Config.API_BASE_URL + p._links.comments;
	    };
	    ViewPictureDetailsComponent.prototype.setError = function (e) {
	        this.showLoadingScreen = false;
	        this.picture = null;
	        this.errorMessage = "Error loading picture from server.";
	    };
	    ViewPictureDetailsComponent = __decorate([
	        core_1.Component({
	            template: __webpack_require__(20),
	            directives: [
	                picture_img_component_1.PictureImgComponent,
	                user_profile_card_component_1.UserProfileCard,
	                comment_list_component_1.CommentListComponent,
	                post_comment_component_1.PostCommentComponent,
	            ],
	        }), 
	        __metadata('design:paramtypes', [picture_service_1.PictureService, router_1.RouteParams, router_1.Location])
	    ], ViewPictureDetailsComponent);
	    return ViewPictureDetailsComponent;
	}());
	exports.ViewPictureDetailsComponent = ViewPictureDetailsComponent;


/***/ },
/* 30 */
/***/ function(module, exports, __webpack_require__) {

	"use strict";
	var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
	    var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
	    if (typeof Reflect === "object" && typeof Reflect.decorate === "function") r = Reflect.decorate(decorators, target, key, desc);
	    else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
	    return c > 3 && r && Object.defineProperty(target, key, r), r;
	};
	var __metadata = (this && this.__metadata) || function (k, v) {
	    if (typeof Reflect === "object" && typeof Reflect.metadata === "function") return Reflect.metadata(k, v);
	};
	var core_1 = __webpack_require__(1);
	var router_1 = __webpack_require__(2);
	var picture_service_1 = __webpack_require__(3);
	var header_service_1 = __webpack_require__(33);
	var picture_list_component_1 = __webpack_require__(10);
	var pagination_component_1 = __webpack_require__(26);
	var PAGE_SIZE = 12;
	var ViewPictureListComponent = (function () {
	    function ViewPictureListComponent(_pictureService, _headerService, _router, _location, _params) {
	        this._pictureService = _pictureService;
	        this._headerService = _headerService;
	        this._router = _router;
	        this._location = _location;
	        this._params = _params;
	        this.currentPage = +this._params.get("page") || 1;
	        this._requestedPage = 0;
	        this._sort = this._params.get("sort") || "newest";
	    }
	    // public pageChanged(event: IPageChangedEvent):void {
	    //     if (this.currentPage === +this._params.get('page')) {
	    //         this.loadPictures(event.page);
	    //     } else {
	    //         setTimeout(() => {
	    //             const instruction = this._router.generate(['PictureList', { sort: this._sort, page: event.page }]);
	    //             this._router.navigateByInstruction(instruction);
	    //         }, 0);
	    //     }
	    // }
	    ViewPictureListComponent.prototype.pageChanged = function (event) {
	        if (this.currentPage !== event.page) {
	            var instruction = this.linkPage(this._sort, event.page);
	            var path = instruction.toUrlPath();
	            var qs = instruction.toUrlQuery();
	            this._location.go(path, qs);
	        }
	        else if (this._requestedPage !== this.currentPage) {
	            // avoid double fetch with two pager components on the page
	            this.loadPictures(event.page);
	            this._requestedPage = event.page;
	        }
	    };
	    ViewPictureListComponent.prototype.ngOnInit = function () {
	        this.loadPictures(this.currentPage);
	        this._headerService.clear();
	        this._headerService.addLinkRel("canonical", this.canonicalUrl());
	        if (this.currentPage > 1) {
	            this._headerService.addLinkRel("prev", this.urlFor(this._sort, this.currentPage - 1));
	        }
	        this._headerService.addLinkRel("next", this.urlFor(this._sort, this.currentPage + 1));
	    };
	    ViewPictureListComponent.prototype.isSort = function (sort) {
	        return this._sort === sort;
	    };
	    ViewPictureListComponent.prototype.linkPage = function (sort, page) {
	        return this._router.generate(['PictureList', { sort: sort, page: page }]);
	    };
	    ViewPictureListComponent.prototype.urlFor = function (sort, page) {
	        var instruction = this.linkPage(sort, page);
	        return instruction.toLinkUrl();
	    };
	    ViewPictureListComponent.prototype.canonicalUrl = function () {
	        var i = this._router.generate(['PictureList']);
	        return i.toLinkUrl();
	    };
	    ViewPictureListComponent.prototype.loadPictures = function (page) {
	        var _this = this;
	        this._pictureService.listPictures({
	            count: PAGE_SIZE,
	            offset: PAGE_SIZE * (page - 1),
	            sort: this._sort,
	        }).subscribe(function (list) {
	            _this.pictures = list.pictures;
	            if (_this.totalCount === undefined)
	                _this.totalCount = list.count;
	        }, function (error) { return _this.errorMessage = error; });
	    };
	    __decorate([
	        core_1.ViewChild(pagination_component_1.Pagination), 
	        __metadata('design:type', pagination_component_1.Pagination)
	    ], ViewPictureListComponent.prototype, "_pagination", void 0);
	    ViewPictureListComponent = __decorate([
	        core_1.Component({
	            template: __webpack_require__(21),
	            directives: [
	                router_1.ROUTER_DIRECTIVES,
	                picture_list_component_1.PictureListComponent,
	                pagination_component_1.Pagination,
	            ],
	        }), 
	        __metadata('design:paramtypes', [picture_service_1.PictureService, header_service_1.HeaderService, router_1.Router, router_1.Location, router_1.RouteParams])
	    ], ViewPictureListComponent);
	    return ViewPictureListComponent;
	}());
	exports.ViewPictureListComponent = ViewPictureListComponent;


/***/ },
/* 31 */
/***/ function(module, exports, __webpack_require__) {

	"use strict";
	var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
	    var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
	    if (typeof Reflect === "object" && typeof Reflect.decorate === "function") r = Reflect.decorate(decorators, target, key, desc);
	    else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
	    return c > 3 && r && Object.defineProperty(target, key, r), r;
	};
	var __metadata = (this && this.__metadata) || function (k, v) {
	    if (typeof Reflect === "object" && typeof Reflect.metadata === "function") return Reflect.metadata(k, v);
	};
	var core_1 = __webpack_require__(1);
	var router_1 = __webpack_require__(2);
	var view_picture_list_component_1 = __webpack_require__(30);
	var view_picture_details_component_1 = __webpack_require__(29);
	var picture_config_service_1 = __webpack_require__(4);
	var picture_service_1 = __webpack_require__(3);
	var ViewPicturesComponent = (function () {
	    function ViewPicturesComponent() {
	    }
	    ViewPicturesComponent = __decorate([
	        core_1.Component({
	            template: "\n    <router-outlet></router-outlet>\n  ",
	            directives: [
	                router_1.RouterOutlet
	            ],
	            providers: [
	                picture_config_service_1.PictureConfigService,
	                picture_service_1.PictureService,
	            ]
	        }),
	        router_1.RouteConfig([
	            {
	                name: 'PictureList',
	                path: "/",
	                component: view_picture_list_component_1.ViewPictureListComponent,
	                useAsDefault: true,
	            },
	            {
	                name: 'PictureDetails',
	                path: '/pic/:slug',
	                component: view_picture_details_component_1.ViewPictureDetailsComponent,
	            },
	        ]), 
	        __metadata('design:paramtypes', [])
	    ], ViewPicturesComponent);
	    return ViewPicturesComponent;
	}());
	exports.ViewPicturesComponent = ViewPicturesComponent;


/***/ },
/* 32 */
/***/ function(module, exports, __webpack_require__) {

	"use strict";
	var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
	    var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
	    if (typeof Reflect === "object" && typeof Reflect.decorate === "function") r = Reflect.decorate(decorators, target, key, desc);
	    else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
	    return c > 3 && r && Object.defineProperty(target, key, r), r;
	};
	var __metadata = (this && this.__metadata) || function (k, v) {
	    if (typeof Reflect === "object" && typeof Reflect.metadata === "function") return Reflect.metadata(k, v);
	};
	var core_1 = __webpack_require__(1);
	var dyn_img_component_1 = __webpack_require__(8);
	var user_profile_link_directive_1 = __webpack_require__(5);
	var UserProfileCard = (function () {
	    function UserProfileCard() {
	        this.user = { image: {} };
	    }
	    __decorate([
	        core_1.Input(), 
	        __metadata('design:type', Object)
	    ], UserProfileCard.prototype, "user", void 0);
	    __decorate([
	        core_1.Input(), 
	        __metadata('design:type', String)
	    ], UserProfileCard.prototype, "styleClass", void 0);
	    UserProfileCard = __decorate([
	        core_1.Component({
	            selector: 'user-profile-card',
	            template: "\n        <div class=\"card profile\">\n            <!--a class=\"hidden-xs hidden-sm col-md-4 avatar\" [userProfileLink]=\"user\">\n                <img src=\"http://placehold.it/100x100\">\n            </a-->\n            <a class=\"col-xs-12 col-md-8 name\" [userProfileLink]=\"user\">\n                <div class=\"fullname\">{{user?.name}}</div>\n                <div class=\"username\">@{{user?.username}}</div>\n            </a>\n        </div>",
	            directives: [
	                dyn_img_component_1.DynImgComponent,
	                user_profile_link_directive_1.UserProfileLink,
	            ],
	        }), 
	        __metadata('design:paramtypes', [])
	    ], UserProfileCard);
	    return UserProfileCard;
	}());
	exports.UserProfileCard = UserProfileCard;


/***/ },
/* 33 */
/***/ function(module, exports, __webpack_require__) {

	"use strict";
	var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
	    var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
	    if (typeof Reflect === "object" && typeof Reflect.decorate === "function") r = Reflect.decorate(decorators, target, key, desc);
	    else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
	    return c > 3 && r && Object.defineProperty(target, key, r), r;
	};
	var __metadata = (this && this.__metadata) || function (k, v) {
	    if (typeof Reflect === "object" && typeof Reflect.metadata === "function") return Reflect.metadata(k, v);
	};
	var core_1 = __webpack_require__(1);
	// TODO: better use BrowserDomAdapter, see
	// http://stackoverflow.com/questions/30059137/angular-2-component-access-dom-or-create-component-without-template-purely-f
	var HeaderService = (function () {
	    function HeaderService() {
	        this._elements = [];
	        this._head = document.querySelector("head");
	    }
	    HeaderService.prototype.clear = function () {
	        var _this = this;
	        if (!this._head)
	            return null;
	        this._elements.forEach(function (e) { return _this._head.removeChild(e); });
	        this._elements = [];
	    };
	    HeaderService.prototype.addHeaderElement = function (type) {
	        if (!this._head)
	            return null;
	        var element = document.createElement(type);
	        this._head.appendChild(element);
	        this._elements.push(element);
	        return element;
	    };
	    HeaderService.prototype.addLinkRel = function (type, href) {
	        if (!this._head)
	            return null;
	        var link = this.addHeaderElement("link");
	        link['rel'] = type;
	        link['href'] = href;
	        return link;
	    };
	    HeaderService = __decorate([
	        core_1.Injectable(), 
	        __metadata('design:paramtypes', [])
	    ], HeaderService);
	    return HeaderService;
	}());
	exports.HeaderService = HeaderService;


/***/ },
/* 34 */
/***/ function(module, exports) {

	module.exports = require("angular2-universal-preview");

/***/ },
/* 35 */
/***/ function(module, exports) {

	module.exports = require("angular2-universal-preview/polyfills");

/***/ },
/* 36 */
/***/ function(module, exports) {

	module.exports = require("angular2/common");

/***/ },
/* 37 */
/***/ function(module, exports) {

	module.exports = require("express");

/***/ },
/* 38 */
/***/ function(module, exports) {

	module.exports = require("path");

/***/ }
/******/ ]);