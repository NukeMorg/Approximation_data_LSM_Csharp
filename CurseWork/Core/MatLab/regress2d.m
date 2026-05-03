% Построение 2D полиномиальной регрессии
% Ожидает в workspace переменные: x, y, degree

p = polyfit(x, y, degree);
y_pred = polyval(p, x);

% Формируем строку уравнения
eq_str = 'y = ';
for k = 1:length(p)
    coef = p(k);
    pow = length(p) - k;
    if k == 1
        if coef < 0
            eq_str = [eq_str, '- '];
            coef = -coef;
        end
    else
        if coef >= 0
            eq_str = [eq_str, ' + '];
        else
            eq_str = [eq_str, ' - '];
            coef = -coef;
        end
    end
    if pow == 0
        eq_str = [eq_str, num2str(coef, '%.4f')];
    elseif pow == 1
        eq_str = [eq_str, num2str(coef, '%.4f'), '·x'];
    else
        eq_str = [eq_str, num2str(coef, '%.4f'), '·x^{', num2str(pow), '}'];
    end
end

figure('Name', 'MATLAB 2D регрессия', 'NumberTitle', 'off', ...
       'Position', [100 100 800 600]);
plot(x, y, 'bo', 'DisplayName', 'Данные');
hold on;
plot(x, y_pred, 'r-', 'LineWidth', 1.5, ...
     'DisplayName', ['Модель (степень ', num2str(degree), ')']);
hold off;
legend('Location', 'best');
xlabel('X'); ylabel('Y');
grid on;
title(sprintf('Полиномиальная регрессия степени %d', degree));

annotation('textbox', [0.15, 0.02, 0.7, 0.08], ...
           'String', eq_str, ...
           'FontSize', 10, ...
           'BackgroundColor', 'white', ...
           'EdgeColor', 'black', ...
           'HorizontalAlignment', 'center', ...
           'VerticalAlignment', 'middle');
drawnow;