% Построение 3D поверхности
% Ожидает в workspace переменные: x, y, z, surface_type

figure('Name', 'MATLAB 3D поверхность', 'NumberTitle', 'off', ...
       'Position', [100 100 800 600]);
hold on;
scatter3(x, y, z, 30, 'b', 'filled', 'DisplayName', 'Данные');

if strcmp(surface_type, 'plane')
    A = [x(:), y(:), ones(length(x),1)];
    coeff = A \ z(:);
    a = coeff(1); b = coeff(2); c = coeff(3);
    [Xg, Yg] = meshgrid(linspace(min(x), max(x), 20), ...
                       linspace(min(y), max(y), 20));
    Zg = a*Xg + b*Yg + c;
    surf(Xg, Yg, Zg, 'FaceAlpha', 0.4, 'EdgeColor', 'none', ...
         'DisplayName', 'Плоскость');
    title('3D регрессия: плоскость');
    eq_str = sprintf('z = %.4f·x + %.4f·y + %.4f', a, b, c);
else
    X = [x(:).^2, y(:).^2, x(:).*y(:), x(:), y(:), ones(length(x),1)];
    coeff = X \ z(:);
    a=coeff(1); b=coeff(2); c_xy=coeff(3); d=coeff(4); e=coeff(5); f=coeff(6);
    [Xg, Yg] = meshgrid(linspace(min(x), max(x), 20), ...
                       linspace(min(y), max(y), 20));
    Zg = a*Xg.^2 + b*Yg.^2 + c_xy*Xg.*Yg + d*Xg + e*Yg + f;
    surf(Xg, Yg, Zg, 'FaceAlpha', 0.4, 'EdgeColor', 'none', ...
         'DisplayName', 'Квадрика');
    title('3D регрессия: полная квадрика');
    eq_str = sprintf('z = %.4f x^2 + %.4f y^2 + %.4f xy + %.4f x + %.4f y + %.4f', ...
                     a, b, c_xy, d, e, f);
end
hold off;
xlabel('X'); ylabel('Y'); zlabel('Z');
legend('Location', 'best');
grid on;
view(135, 30);

annotation('textbox', [0.15, 0.02, 0.7, 0.08], ...
           'String', eq_str, ...
           'FontSize', 10, ...
           'BackgroundColor', 'white', ...
           'EdgeColor', 'black', ...
           'HorizontalAlignment', 'center', ...
           'VerticalAlignment', 'middle');
drawnow;